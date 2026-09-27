using Distill.Core.Runs;
using Distill.Testing.Abstractions;
using Distill.Testing.MTP;
using Distill.Tests.Golden;

namespace Distill.Tests.Integration;

public class MtpReportIntegrationTests
{
    [Fact]
    public async Task ExecuteWithProcessAsync_OnMtpSample_CollectsTrxEvidence()
    {
        var sampleRoot = Path.Combine(GoldenPaths.RepoRoot, "samples", "MtpFailSample");
        var projectPath = Path.Combine(sampleRoot, "MtpFailSample.csproj");
        var runDirectory = Path.Combine(Path.GetTempPath(), "distill-mtp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runDirectory);

        var context = new DistillRunContext
        {
            RunId = "integration",
            WorkspaceRoot = sampleRoot,
            RunDirectory = runDirectory,
            Profile = "quick"
        };

        var check = new TestCheckDefinition(
            Id: "unit",
            Target: projectPath,
            Arguments: Array.Empty<string>());

        var source = new MtpReportResultSource();
        var execution = await source.ExecuteWithProcessAsync(check, context, CancellationToken.None);

        Assert.True(execution.Evidence.Failed > 0);
        Assert.Contains(
            execution.Evidence.Cases,
            testCase => testCase.Name.Contains("AlwaysFails", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(runDirectory, "unit", "fallback.trx")));
        Assert.True(File.Exists(Path.Combine(runDirectory, "unit", "diagnostics.json")));
        Assert.Equal("mtp-report", execution.SourceId);
    }
}
