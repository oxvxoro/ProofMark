using System.Diagnostics;
using Proof.Adapters.CodeMap;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

/// <summary>
/// CodeMap AspNetFixture의 실제 앱 그래프로 P010을 끝까지 본다.
/// 이 실행의 정책만 <c>impact.profile: app</c>, <c>appContract: required</c>이고,
/// 저장소 <c>proof.yml</c>은 P010을 계속 끈다. 커버리지는 테스트 실행 대신
/// 고정 Cobertura 산출물로 준다. 산출물이 없으면 P010은 닫히지 않는다.
/// </summary>
[Collection("CodeMapInProcessIndex")]
public sealed class P010AspNetGoldenTests
{
    private const string ChangedFile = "Handlers.cs";

    [Fact(Timeout = 300_000)]
    public async Task AppProfile_RouteHandlerChange_ClosesP010OnlyWithRuntimeCoverage()
    {
        var root = CopyRestoredFixture();
        try
        {
            var impact = await AnalyzeHandlerChangeAsync(root);
            var plan = new DeterministicProofPlanner().Plan(impact, new ProofPolicy
            {
                AppContractRequired = true,
                PublicApiCompatibilityRequired = false,
                InternalConsumerCompatibilityRequired = false,
                TestMappingRequired = false,
            });
            var p010 = plan.Obligations.Where(item => item.RuleId == "P010").ToArray();
            Assert.True(p010.Length > 0, "expected P010 from the app graph; got " + Describe(plan));
            Assert.Contains(p010, item => item.SubjectId.StartsWith("route://", StringComparison.Ordinal));

            var request = Request(root);
            var producer = new RuntimeCoverageEvidenceProducer();
            var unrelated = WriteCoverage(root, "unrelated.cobertura.xml", ("AspNetFixture.Handlers", "PingHandler"));
            var unrelatedEvidence = await producer.AnalyzeAsync(request, impact, plan, [unrelated], CancellationToken.None);
            Assert.DoesNotContain(unrelatedEvidence, item => p010.Any(obligation => obligation.SubjectId == item.Subject));
            var uncovered = new DeterministicProofEvaluator().Evaluate(plan, new EvidenceBinder().Bind(plan, []));
            Assert.All(
                uncovered.Obligations.Where(item => item.Obligation.RuleId == "P010"),
                item => Assert.NotEqual(ObligationStatus.Proven, item.Status));

            var coveragePath = WriteCoverage(root, "coverage.cobertura.xml", ("AspNetFixture.Handlers", "GetOrderHandler"));
            var evidence = await producer.AnalyzeAsync(request, impact, plan, [coveragePath], CancellationToken.None);
            var bound = new EvidenceBinder().Bind(plan, evidence);
            var evaluation = new DeterministicProofEvaluator().Evaluate(plan, bound);

            foreach (var obligation in p010)
            {
                var link = Assert.Single(bound.Links, item => item.ObligationId == obligation.Id);
                Assert.Equal("BIND_APP_COVERAGE", link.BindingRuleId);
                Assert.Equal(
                    ObligationStatus.Proven,
                    Assert.Single(evaluation.Obligations, item => item.Obligation.Id == obligation.Id).Status);
            }
        }
        finally
        {
            Cleanup(root);
        }
    }

    private static async Task<ChangeImpact> AnalyzeHandlerChangeAsync(string root)
    {
        var line = File.ReadAllLines(Path.Combine(root, ChangedFile))
            .Select((text, index) => (text, number: index + 1))
            .Single(item => item.text.Contains("GetOrderHandler(", StringComparison.Ordinal))
            .number;
        var span = new LineSpan(ChangedFile, line, line);
        var request = Request(root) with
        {
            Spans = [span],
            FileDeltas = [new FileDelta(FileChangeKind.Modified, ChangedFile, ChangedFile, [span], [span])]
        };
        var provider = new CodeMapChangeImpactProvider(new CodeMapAnalysisOptions(
            root,
            Path.Combine(root, "AspNetFixture.csproj"),
            new ImpactAnalysisSettings(Profile: "app")));
        return await provider.AnalyzeAsync(request, CancellationToken.None);
    }

    private static ChangeRequest Request(string root)
        => new(root, "base", "head", [], SourceDigest: "aspnet-p010");

    // coverlet이 내는 모양의 Cobertura. 한 줄 적중이 메서드 실행을 뜻한다.
    private static string WriteCoverage(string root, string fileName, params (string Type, string Method)[] methods)
    {
        var classes = string.Concat(methods.Select(item =>
            $"""<class name="{item.Type}" filename="{ChangedFile}"><methods><method name="{item.Method}" signature="()"><lines><line number="1" hits="1" /></lines></method></methods></class>"""));
        var path = Path.Combine(root, fileName);
        File.WriteAllText(path, $"""<?xml version="1.0"?><coverage><packages><package name="AspNetFixture"><classes>{classes}</classes></package></packages></coverage>""");
        return path;
    }

    private static string Describe(ProofPlan plan)
        => string.Join("; ", plan.Obligations.Select(item => $"{item.RuleId} {item.SubjectId}"));

    private static string CopyRestoredFixture()
    {
        var source = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "engines", "codemap", "tests", "Fixtures", "AspNetFixture"));
        var root = Path.Combine(Path.GetTempPath(), "proof-p010-aspnet-" + Guid.NewGuid().ToString("N"));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var first = relative.Split(Path.DirectorySeparatorChar)[0];
            if (first is "bin" or "obj" or ".codemap")
            {
                continue;
            }

            var target = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        Restore(root);
        return root;
    }

    private static void Restore(string root)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "restore",
            WorkingDirectory = root,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("dotnet restore did not start");
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(120)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("dotnet restore timed out for AspNetFixture");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"dotnet restore failed ({process.ExitCode}): {stdout.Result}{stderr.Result}");
        }
    }

    // SqliteConnection.ClearAllPools()를 부르지 않는다. 다른 테스트의 index.db를 체크포인트해
    // 인덱스 스탬프(길이, 쓰기 시각)를 깨뜨린다.
    private static void Cleanup(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
