using Distill.Core.Runs;
using Distill.Runner;
using Distill.Testing.Abstractions;
using Distill.Testing.VSTest;

namespace Distill.Tests.Integration;

public class VstestLoggerIntegrationTests
{
    [Fact]
    public async Task CompositeSource_OnSampleProject_CollectsStructuredEvidence()
    {
        var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var runDirectory = Path.Combine(Path.GetTempPath(), "distill-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runDirectory);

        var context = new DistillRunContext
        {
            RunId = "integration",
            WorkspaceRoot = workspace,
            RunDirectory = runDirectory
        };

        var check = new TestCheckDefinition(
            Id: "unit",
            Target: Path.Combine(workspace, "samples", "TestFailSample", "TestFailSample.csproj"),
            Arguments: Array.Empty<string>());

        var source = new VstestCompositeResultSource();
        var evidence = await source.ExecuteAndCollectAsync(check, context, CancellationToken.None);

        Assert.True(evidence.Failed > 0);
        Assert.Contains(evidence.Cases, testCase => testCase.Name.Contains("AlwaysFails", StringComparison.Ordinal));
        Assert.True(File.Exists(RunArtifactLayout.GetUnitEventsPath(runDirectory)));
        Assert.True(File.Exists(RunArtifactLayout.GetUnitTrxPath(runDirectory)));
    }

    [Fact]
    public async Task ExecuteWithProcessAsync_UsesSingleProcessForDualArtifacts()
    {
        var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var runDirectory = Path.Combine(Path.GetTempPath(), "distill-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runDirectory);

        var context = new DistillRunContext
        {
            RunId = "integration",
            WorkspaceRoot = workspace,
            RunDirectory = runDirectory
        };

        var check = new TestCheckDefinition(
            Id: "unit",
            Target: Path.Combine(workspace, "samples", "TestFailSample", "TestFailSample.csproj"),
            Arguments: Array.Empty<string>());

        var source = new VstestCompositeResultSource();
        var execution = await source.ExecuteWithProcessAsync(check, context, CancellationToken.None);

        Assert.True(execution.Evidence.Failed > 0);
        Assert.Equal(ProcessStatus.Completed, execution.ProcessResult.Status);
        Assert.True(File.Exists(RunArtifactLayout.GetUnitEventsPath(runDirectory)));
        Assert.True(File.Exists(RunArtifactLayout.GetUnitTrxPath(runDirectory)));
        Assert.False(File.Exists(Path.Combine(runDirectory, "unit", "stdout.log")));
    }
}
