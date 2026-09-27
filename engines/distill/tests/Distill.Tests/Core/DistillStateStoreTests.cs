using System.Text.Json;
using Distill.Core.Runs;

namespace Distill.Tests.Core;

public class DistillStateStoreTests
{
    [Fact]
    public async Task SaveAsync_ConcurrentWrites_ProducesValidStateJson()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"distill-state-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);

        try
        {
            var tasks = Enumerable.Range(0, 32)
                .Select(index => DistillStateStore.SaveAsync(
                    workspace,
                    new DistillState(
                        $"run-{index}",
                        Path.Combine(workspace, ".distill", "runs", $"run-{index}"),
                        DateTimeOffset.UtcNow),
                    CancellationToken.None))
                .ToArray();

            var exception = await Record.ExceptionAsync(() => Task.WhenAll(tasks));
            Assert.Null(exception);

            var statePath = RunArtifactLayout.GetStatePath(workspace);
            Assert.True(File.Exists(statePath));

            var json = await File.ReadAllTextAsync(statePath);
            var state = JsonSerializer.Deserialize<DistillState>(json);
            Assert.NotNull(state);
            Assert.False(string.IsNullOrWhiteSpace(state!.LastRunId));
            Assert.False(string.IsNullOrWhiteSpace(state.LastRunDirectory));
            Assert.NotNull(state.LastRunAt);
        }
        finally
        {
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SaveLatestAsync_OlderCandidate_DoesNotReplaceNewerState()
    {
        var workspace = CreateWorkspace();
        try
        {
            var newerAt = new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);
            var olderAt = newerAt.AddHours(-1);
            await DistillStateStore.SaveLatestAsync(
                workspace,
                new DistillState("run-newer", RunDirectory(workspace, "run-newer"), newerAt));

            await DistillStateStore.SaveLatestAsync(
                workspace,
                new DistillState("run-older", RunDirectory(workspace, "run-older"), olderAt));

            var state = await DistillStateStore.LoadAsync(workspace);
            Assert.NotNull(state);
            Assert.Equal("run-newer", state!.LastRunId);
            Assert.Equal(newerAt, state.LastRunAt);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task SaveLatestAsync_NewerCandidate_ReplacesOlderState()
    {
        var workspace = CreateWorkspace();
        try
        {
            var olderAt = new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);
            var newerAt = olderAt.AddHours(1);
            await DistillStateStore.SaveLatestAsync(
                workspace,
                new DistillState("run-older", RunDirectory(workspace, "run-older"), olderAt));

            await DistillStateStore.SaveLatestAsync(
                workspace,
                new DistillState("run-newer", RunDirectory(workspace, "run-newer"), newerAt));

            var state = await DistillStateStore.LoadAsync(workspace);
            Assert.NotNull(state);
            Assert.Equal("run-newer", state!.LastRunId);
            Assert.Equal(newerAt, state.LastRunAt);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task SaveLatestAsync_MalformedExistingState_ReplacesWithValidCandidate()
    {
        var workspace = CreateWorkspace();
        try
        {
            var statePath = RunArtifactLayout.GetStatePath(workspace);
            Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
            await File.WriteAllTextAsync(statePath, "{ not valid json");

            var candidateAt = new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);
            await DistillStateStore.SaveLatestAsync(
                workspace,
                new DistillState("run-recovered", RunDirectory(workspace, "run-recovered"), candidateAt));

            var state = await DistillStateStore.LoadAsync(workspace);
            Assert.NotNull(state);
            Assert.Equal("run-recovered", state!.LastRunId);
            Assert.Equal(candidateAt, state.LastRunAt);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task SaveLatestAsync_ConcurrentWrites_RetainsGreatestLastRunAt()
    {
        var workspace = CreateWorkspace();
        try
        {
            var baseTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var tasks = Enumerable.Range(0, 32)
                .Select(index =>
                {
                    var timestamp = baseTime.AddMinutes(index);
                    return DistillStateStore.SaveLatestAsync(
                        workspace,
                        new DistillState(
                            $"run-{index}",
                            RunDirectory(workspace, $"run-{index}"),
                            timestamp));
                })
                .ToArray();

            await Task.WhenAll(tasks);

            var state = await DistillStateStore.LoadAsync(workspace);
            Assert.NotNull(state);
            Assert.Equal(baseTime.AddMinutes(31), state!.LastRunAt);
            Assert.Equal("run-31", state.LastRunId);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static string CreateWorkspace()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"distill-state-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        return workspace;
    }

    private static string RunDirectory(string workspace, string runId)
        => Path.Combine(workspace, ".distill", "runs", runId);
}
