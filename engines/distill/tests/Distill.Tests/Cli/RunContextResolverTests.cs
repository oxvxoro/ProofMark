using System.Text.Json;
using System.Text.Json.Serialization;
using Distill.Cli;
using Distill.Core.Runs;

namespace Distill.Tests.Cli;

// Brief 05 — state.json이 없거나 잘못되면, 해석기는
// 사전식 디렉터리 이름 순서보다 FinishedAt이 설정된 최신 완료 매니페스트의
// 실행을 우선해야 한다. FinishedAt을 가진 매니페스트가 전혀 없으면
// (레거시 실행) 디렉터리 이름으로 되돌아간다.
public class RunContextResolverTests
{
    [Fact]
    public async Task ResolveLatestAsync_NoState_PrefersLatestCompletedManifestOverNewerIncompleteDirectory()
    {
        var workspace = CreateWorkspace();
        try
        {
            var completedOlderName = CreateRunDirectory(workspace, "d-20260101-000000-aaaaaa");
            WriteManifest(completedOlderName, "d-20260101-000000-aaaaaa", DateTimeOffset.UtcNow.AddHours(-2));

            var incompleteNewerName = CreateRunDirectory(workspace, "d-20260102-000000-bbbbbb");
            WriteManifest(incompleteNewerName, "d-20260102-000000-bbbbbb", finishedAt: null);

            var resolved = await RunContextResolver.ResolveLatestAsync(workspace);

            Assert.NotNull(resolved);
            Assert.Equal(completedOlderName, resolved!.Value.RunDirectory);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveLatestAsync_MalformedState_FallsBackToLatestCompletedByFinishedAt()
    {
        var workspace = CreateWorkspace();
        try
        {
            Directory.CreateDirectory(Path.Combine(workspace, ".distill"));
            File.WriteAllText(RunArtifactLayout.GetStatePath(workspace), "{ not valid json");

            var earlierCompleted = CreateRunDirectory(workspace, "d-20260101-000000-aaaaaa");
            WriteManifest(earlierCompleted, "d-20260101-000000-aaaaaa", DateTimeOffset.UtcNow.AddHours(-5));

            var laterCompleted = CreateRunDirectory(workspace, "d-20260101-010000-cccccc");
            WriteManifest(laterCompleted, "d-20260101-010000-cccccc", DateTimeOffset.UtcNow.AddHours(-1));

            var resolved = await RunContextResolver.ResolveLatestAsync(workspace);

            Assert.NotNull(resolved);
            Assert.Equal(laterCompleted, resolved!.Value.RunDirectory);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveLatestAsync_NoCompletedManifests_FallsBackToLegacyDirectoryNameOrdering()
    {
        var workspace = CreateWorkspace();
        try
        {
            var legacyOlder = CreateRunDirectory(workspace, "d-20260101-000000-aaaaaa");
            var legacyNewer = CreateRunDirectory(workspace, "d-20260102-000000-bbbbbb");

            var resolved = await RunContextResolver.ResolveLatestAsync(workspace);

            Assert.NotNull(resolved);
            Assert.Equal(legacyNewer, resolved!.Value.RunDirectory);
            Assert.NotEqual(legacyOlder, resolved.Value.RunDirectory);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static string CreateWorkspace()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"distill-resolver-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        return workspace;
    }

    private static string CreateRunDirectory(string workspace, string runId)
    {
        var runDirectory = RunArtifactLayout.GetRunDirectory(workspace, runId);
        Directory.CreateDirectory(runDirectory);
        return runDirectory;
    }

    private static void WriteManifest(string runDirectory, string runId, DateTimeOffset? finishedAt)
    {
        var manifest = new RunManifest(
            runId,
            DateTimeOffset.UtcNow.AddHours(-1),
            "/repo",
            finishedAt,
            finishedAt is null ? null : VerificationStatus.Pass);

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() }
        });
        File.WriteAllText(RunArtifactLayout.GetRunManifestPath(runDirectory), json);
    }
}
