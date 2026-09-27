using Proof.Cli;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

// 이 테스트들은 프로세스 전역의 현재 디렉터리를 바꾼다. xUnit은
// 컬렉션을 직렬로 실행하므로 서로 경합하면 안 된다.
[CollectionDefinition("WorkingDirectory", DisableParallelization = true)]
public sealed class WorkingDirectoryCollection;

[Collection("WorkingDirectory")]
public sealed class MapFromCoverageTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "proof-map-cov-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workspace, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private void WriteProofYaml()
    {
        Directory.CreateDirectory(_workspace);
        File.WriteAllText(Path.Combine(_workspace, "proof.yml"), """
            version: 2
            policy:
              testMaps:
                - symbol: "Existing.Symbol"
                  tests:
                    - Proof.Tests.ExistingTests.Method
            """);
    }

    private string WriteCoverage()
    {
        // RuntimeCoverageTests의 실행된 메서드 형태를 재사용한다. cobertura는
        // 실행된 제품 메서드를 가리키며, 실행 중인 테스트는 절대 가리키지 않는다.
        const string xml = """
            <coverage>
              <packages><package name="App"><classes>
                <class name="App.OrderService" filename="OrderService.cs">
                  <methods>
                    <method name="Cancel"><lines><line number="1" hits="1" /></lines></method>
                    <method name="Refund"><lines><line number="1" hits="0" /></lines></method>
                  </methods>
                </class>
              </classes></package></packages>
            </coverage>
            """;
        var directory = Path.Combine(_workspace, "coverage");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "coverage.cobertura.xml");
        File.WriteAllText(path, xml);
        return Path.GetRelativePath(_workspace, path).Replace('\\', '/');
    }

    private static ProofObligation P005(string subjectId, string displayName)
        => new("O1", "P005", ObligationKind.TestMapping, "mapping", subjectId, true, 2, ["r"],
            new ProofSubject(SubjectKind.Symbol, subjectId, "App", DisplayName: displayName));

    [Fact]
    public void NoCoverageFiles_Returns2()
    {
        Directory.CreateDirectory(_workspace);
        Directory.SetCurrentDirectory(_workspace);
        try
        {
            var exit = MapSuggestCommand.ExecuteFromCoverageAsync([], null, write: false, accept: false, [], CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert.Equal(2, exit);
        }
        finally
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
        }
    }

    [Fact]
    public async Task AdviceWithoutWrite_ReportsHits_AndDoesNotNameTests()
    {
        WriteProofYaml();
        WriteCoverage();
        Directory.SetCurrentDirectory(_workspace);
        try
        {
            var advice = MapSuggestCommand.BuildFromCoverageAdvice(
                [(P005("App.OrderService.Cancel", "App.OrderService.Cancel"), "App.OrderService.Cancel")],
                ["Proof.Tests.MyClass"]);
            var json = System.Text.Json.JsonSerializer.Serialize(advice, ProofJson.WireOptions);

            Assert.Contains("\"mustReview\": true", json, StringComparison.Ordinal);
            Assert.Contains("cobertura does not name the executing test", json, StringComparison.Ordinal);
            Assert.Contains("proof map add --symbol", json, StringComparison.Ordinal);
            // WireOptions는 <와 >를 유니코드로 이스케이프한다. 이스케이프된 형태를 어서션한다.
            Assert.Contains("YOU MUST FILL", json, StringComparison.Ordinal);
            Assert.Contains("Proof.Tests.MyClass", json, StringComparison.Ordinal);
        }
        finally
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
        }
    }

    [Fact]
    public async Task WriteWithoutTest_Returns2()
    {
        WriteProofYaml();
        WriteCoverage();
        Directory.SetCurrentDirectory(_workspace);
        try
        {
            var exit = await MapSuggestCommand.ExecuteFromCoverageAsync(
                [WriteCoverage()], null, write: true, accept: false, tests: [], CancellationToken.None);
            Assert.Equal(2, exit);
            Assert.False(File.Exists(Path.Combine(_workspace, "proof.yml.tmp")));
        }
        finally
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
        }
    }

    [Fact]
    public async Task WriteWithAccept_WritesExplicitTestForHitSymbol()
    {
        WriteProofYaml();
        WriteCoverage();
        Directory.SetCurrentDirectory(_workspace);
        try
        {
            // 히트를, 주체가 cobertura의 class.method 식별과
            // 일치하는 P005 의무에 묶는다.
            var exit = await ExecuteWithPlanAsync(
                [P005("App.OrderService.Cancel", "App.OrderService.Cancel")],
                tests: ["Proof.Tests.OrderServiceTests.Cancel_keeps_order"]);

            Assert.Equal(0, exit);
            var config = ProofConfig.Load(_workspace);
            var entry = Assert.Single(config.Policy.TestMaps.Where(item => item.Symbol.Contains("Cancel", StringComparison.Ordinal)));
            Assert.Contains("Proof.Tests.OrderServiceTests.Cancel_keeps_order", entry.Tests);
        }
        finally
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
        }
    }

    [Fact]
    public async Task WriteWithoutAccept_DoesNotChangeFile()
    {
        WriteProofYaml();
        WriteCoverage();
        var original = File.ReadAllText(Path.Combine(_workspace, "proof.yml"));
        Directory.SetCurrentDirectory(_workspace);
        try
        {
            var exit = await ExecuteWithPlanAsync(
                [P005("App.OrderService.Cancel", "App.OrderService.Cancel")],
                tests: ["Proof.Tests.OrderServiceTests.Cancel_keeps_order"],
                accept: false);

            Assert.Equal(0, exit);
            Assert.Equal(original, File.ReadAllText(Path.Combine(_workspace, "proof.yml")));
        }
        finally
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
        }
    }

    // 명령이 아니면 계획했을 인증서를 써서, 스텁된 P005
    // 계획으로 ExecuteFromCoverageAsync를 구동한다.
    private async Task<int> ExecuteWithPlanAsync(
        ProofObligation[] obligations,
        string[] tests,
        bool accept = true)
    {
        var plan = new ProofPlan(obligations, SourceDigest: "src");
        var evaluation = new ProofEvaluation(
            ProofVerdict.Uncertain,
            [.. obligations.Select(item => new EvaluatedObligation(item, ObligationStatus.Unresolved, []))]);
        var certificate = new ChangeCertificateBuilder().Build(
            new ChangeImpact("base", "head", [], [], [], [], [], "complete", false, SourceDigest: "src"),
            plan,
            new VerificationEvidenceSet([], []),
            evaluation);
        var directory = Path.Combine(_workspace, ".proof", "certificates");
        Directory.CreateDirectory(directory);
        var certificatePath = Path.Combine(directory, "20260101000000-cert.json");
        File.WriteAllText(certificatePath, System.Text.Json.JsonSerializer.Serialize(certificate, ProofJson.WireOptions));

        return await MapSuggestCommand.ExecuteFromCoverageAsync(
            [WriteCoverage()],
            certificatePath,
            write: true,
            accept,
            tests,
            CancellationToken.None);
    }
}
