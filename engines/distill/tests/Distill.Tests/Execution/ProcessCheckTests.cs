using Distill.Core.Config;
using Distill.Core.Planning;
using Distill.Core.Runs;
using Distill.Execution;

namespace Distill.Tests.Execution;

public sealed class ProcessCheckTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), $"distill-process-{Guid.NewGuid():N}");

    public ProcessCheckTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    [Fact]
    public void ResolveCommand_Process_RunsFirstTokenWithoutDotnet()
    {
        var (fileName, arguments) = DistillCheckExecutor.ResolveCommand("node scripts/run.js --name \"a b\"", "process");

        Assert.Equal("node", fileName);
        Assert.Equal(["scripts/run.js", "--name", "a b"], arguments);
    }

    [Fact]
    public void ResolveCommand_OtherKinds_StayDotnet()
    {
        var (fileName, arguments) = DistillCheckExecutor.ResolveCommand("dotnet format --verify-no-changes", "format");

        Assert.Equal("dotnet", fileName);
        Assert.Equal(["format", "--verify-no-changes"], arguments);
    }

    [Fact]
    public async Task ExecuteAsync_Process_ProducesExitCodeOnly()
    {
        var command = OperatingSystem.IsWindows() ? "cmd /c exit 3" : "sh -c \"exit 3\"";

        var result = await ExecuteAsync(new CheckConfig { Kind = "process", Command = command, Timeout = 60 });

        Assert.Equal(VerificationStatus.Fail, result.Status);
        Assert.Equal(3, result.ExitCode);
        Assert.Null(result.ExecutedCases);
    }

    [Fact]
    public async Task ExecuteAsync_JUnitReportMissing_IsUncertainWithoutCases()
    {
        var result = await ExecuteAsync(new CheckConfig
        {
            Kind = "process",
            Command = "dotnet --version",
            Source = "junit",
            Artifact = "reports/junit.xml",
            Project = "scip:web",
            Timeout = 60
        });

        Assert.Equal(VerificationStatus.Uncertain, result.Status);
        Assert.Null(result.ExecutedCases);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "JUNIT_REPORT_MISSING");
    }

    [Fact]
    public void ReadJUnitReport_MapsOutcomesAndProject()
    {
        var path = WriteReport("""
            <testsuites>
              <testsuite name="web">
                <testcase classname="tests.cart" name="adds_item" time="0.25" />
                <testcase classname="tests.cart" name="removes_item"><failure message="boom">trace</failure></testcase>
                <testcase classname="tests.cart" name="later"><skipped /></testcase>
                <testcase name="bare" />
              </testsuite>
            </testsuites>
            """);

        var error = DistillCheckExecutor.ReadJUnitReport(path, "scip:web", DateTimeOffset.UtcNow.AddMinutes(-1), out var cases);

        Assert.Null(error);
        Assert.NotNull(cases);
        Assert.Collection(
            cases,
            item =>
            {
                Assert.Equal("tests.cart.adds_item", item.FullyQualifiedName);
                Assert.Equal("Passed", item.Outcome);
                Assert.Equal(250, item.DurationMs);
                Assert.Equal("scip:web", item.Project);
            },
            item =>
            {
                Assert.Equal("Failed", item.Outcome);
                Assert.Equal("boom", item.Message);
                Assert.Equal("trace", item.Stack);
            },
            item => Assert.Equal("Skipped", item.Outcome),
            item => Assert.Equal("bare", item.FullyQualifiedName));
    }

    [Fact]
    public void ReadJUnitReport_WrittenBeforeRun_IsStale()
    {
        var path = WriteReport("<testsuite><testcase classname=\"a\" name=\"b\" /></testsuite>");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-1));

        var error = DistillCheckExecutor.ReadJUnitReport(path, "scip:web", DateTimeOffset.UtcNow, out var cases);

        Assert.Equal("JUNIT_REPORT_STALE", error);
        Assert.Null(cases);
    }

    [Fact]
    public void ReadJUnitReport_InvalidXml_IsUnreadable()
    {
        var path = WriteReport("<testsuite><testcase");

        var error = DistillCheckExecutor.ReadJUnitReport(path, "scip:web", DateTimeOffset.UtcNow.AddMinutes(-1), out var cases);

        Assert.Equal("JUNIT_REPORT_UNREADABLE", error);
        Assert.Null(cases);
    }

    [Theory]
    [InlineData("kind: process\n    command: npm test\n    source: junit\n    project: scip:web", "artifact or project is empty")]
    [InlineData("kind: process\n    command: npm test\n    artifact: r.xml", "without source junit")]
    [InlineData("kind: test\n    command: dotnet test App.Tests.csproj\n    coverage: c.xml", "is not kind process")]
    [InlineData("kind: test\n    command: dotnet test App.Tests.csproj\n    source: junit", "is not kind process")]
    public void LoadFromYaml_InvalidProcessKeys_Throw(string check, string expected)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => DistillConfigLoader.LoadFromYaml(Yaml(check)));

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadFromYaml_JUnitProcess_Loads()
    {
        var config = DistillConfigLoader.LoadFromYaml(Yaml(
            "kind: process\n    command: npm test\n    source: junit\n    artifact: web/junit.xml\n    project: scip:web\n    coverage: web/cobertura.xml"));

        var check = config.Checks["web"];
        Assert.Equal("web/junit.xml", check.Artifact);
        Assert.Equal("scip:web", check.Project);
        Assert.Equal("web/cobertura.xml", check.Coverage);
    }

    private static string Yaml(string check) => $"""
        version: 1
        profiles:
          quick:
            checks: [web]
        checks:
          web:
            {check}
        """;

    private string WriteReport(string xml)
    {
        var path = Path.Combine(_workspace, $"junit-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, xml);
        return path;
    }

    private async Task<CheckRunResult> ExecuteAsync(CheckConfig definition)
    {
        var context = new DistillRunContext
        {
            RunId = "d-process-test",
            WorkspaceRoot = _workspace,
            RunDirectory = Path.Combine(_workspace, ".distill", "runs", "d-process-test"),
            Profile = "quick"
        };
        Directory.CreateDirectory(context.RunDirectory);

        return await new DistillCheckExecutor().ExecuteAsync(
            new PlannedCheck("web", definition, []),
            context,
            CancellationToken.None);
    }
}
