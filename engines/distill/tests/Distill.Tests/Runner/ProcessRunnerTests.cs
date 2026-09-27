using Distill.Core.Runs;
using Distill.Runner;

namespace Distill.Tests.Runner;

public class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_DotnetVersion_WritesStdoutFile()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "distill-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        var stdoutPath = Path.Combine(tempDirectory, "stdout.log");
        var stderrPath = Path.Combine(tempDirectory, "stderr.log");

        var runner = new ProcessRunner();
        var result = await runner.RunAsync(
            new ProcessSpec(
                FileName: "dotnet",
                Arguments: ["--version"],
                WorkingDirectory: tempDirectory,
                Timeout: TimeSpan.FromSeconds(30),
                StdoutPath: stdoutPath,
                StderrPath: stderrPath));

        Assert.Equal(ProcessStatus.Completed, result.Status);
        Assert.True(File.Exists(stdoutPath));
        Assert.False(string.IsNullOrWhiteSpace(await File.ReadAllTextAsync(stdoutPath)));
    }

    [Fact]
    public async Task RunAsync_Timeout_KillsProcess()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "distill-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        var runner = new ProcessRunner();
        var result = await runner.RunAsync(
            new ProcessSpec(
                FileName: "dotnet",
                Arguments: ["--version"],
                WorkingDirectory: tempDirectory,
                Timeout: TimeSpan.FromMilliseconds(1),
                StdoutPath: Path.Combine(tempDirectory, "stdout.log"),
                StderrPath: Path.Combine(tempDirectory, "stderr.log")));

        Assert.Equal(ProcessStatus.TimedOut, result.Status);
    }

    [Fact]
    public async Task RunAsync_Cancellation_ReturnsCanceled()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "distill-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var runner = new ProcessRunner();
        var result = await runner.RunAsync(
            new ProcessSpec(
                FileName: "dotnet",
                Arguments: ["--version"],
                WorkingDirectory: tempDirectory,
                StdoutPath: Path.Combine(tempDirectory, "stdout.log"),
                StderrPath: Path.Combine(tempDirectory, "stderr.log")),
            cts.Token);

        Assert.Equal(ProcessStatus.Canceled, result.Status);
    }
}

public class RunArtifactLayoutTests
{
    [Fact]
    public void RunPaths_FollowExpectedLayout()
    {
        var workspace = @"C:\repo";
        var runId = RunIdGenerator.Create(new DateTimeOffset(2026, 9, 5, 4, 8, 12, TimeSpan.Zero));

        Assert.StartsWith("d-20260905-040812-", runId);
        Assert.Equal(
            Path.Combine(workspace, ".distill", "runs", runId, "build", "build.binlog"),
            RunArtifactLayout.GetBuildBinlogPath(RunArtifactLayout.GetRunDirectory(workspace, runId)));
    }

    [Fact]
    public void GetCheckDirectory_SanitizesProjectScopedCheckIds()
    {
        var runDirectory = Path.Combine("C:\\repo", ".distill", "runs", "run-1");

        var path = RunArtifactLayout.GetCheckDirectory(runDirectory, "build::Proof.Adapters.CodeMap");

        Assert.Equal(Path.Combine(runDirectory, "build__Proof.Adapters.CodeMap"), path);
    }
}
