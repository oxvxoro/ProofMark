using System.Text.Json;
using System.Text.Json.Serialization;
using Distill.Core.Runs;

namespace Distill.Tests.Core;

// Brief 05 — 매니페스트가 아직 활성(FinishedAt == null)이거나 매니페스트가
// 없거나 형식이 잘못된 실행은, 다른 정리 규칙(기간/개수)이
// 그 실행을 고르더라도 삭제하면 안 된다.
public class RunRetentionTests
{
    [Fact]
    public void Prune_ActiveRunManifest_IsPreserved()
    {
        var workspace = CreateWorkspace();
        try
        {
            var activeRun = CreateRunDirectory(workspace, "d-active", ageDays: 30);
            WriteManifest(activeRun, "d-active", finishedAt: null);

            RunRetention.Prune(workspace, keepLast: 0, maxAgeDays: 1);

            Assert.True(Directory.Exists(activeRun));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public void Prune_MissingManifest_IsTreatedAsLegacyAndSubjectToExistingRules()
    {
        // 매니페스트가 전혀 없는 경우(레거시 실행, 또는 초기 매니페스트 쓰기가
        // 일어나지 않은 실행)는 활성으로 보지 않고 기존 기간/개수 규칙의 적용을 받는다.
        // ReliabilityTests.RunRetention_KeepsRecentRunsAndDeletesOldRuns와 맞춘다.
        // 그 테스트는 매니페스트 없는 디렉터리를 심고 기간/개수로 정리되기를 기대한다.
        var workspace = CreateWorkspace();
        try
        {
            var noManifestRun = CreateRunDirectory(workspace, "d-no-manifest", ageDays: 30);

            RunRetention.Prune(workspace, keepLast: 0, maxAgeDays: 1);

            Assert.False(Directory.Exists(noManifestRun));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public void Prune_MalformedManifest_IsPreservedConservatively()
    {
        var workspace = CreateWorkspace();
        try
        {
            var malformedRun = CreateRunDirectory(workspace, "d-malformed", ageDays: 30);
            File.WriteAllText(RunArtifactLayout.GetRunManifestPath(malformedRun), "{ not valid json");

            RunRetention.Prune(workspace, keepLast: 0, maxAgeDays: 1);

            Assert.True(Directory.Exists(malformedRun));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public void Prune_CompletedOldRun_IsDeletedByExistingRules()
    {
        var workspace = CreateWorkspace();
        try
        {
            var completedRun = CreateRunDirectory(workspace, "d-completed", ageDays: 30);
            WriteManifest(completedRun, "d-completed", finishedAt: DateTimeOffset.UtcNow.AddDays(-30));

            RunRetention.Prune(workspace, keepLast: 0, maxAgeDays: 1);

            Assert.False(Directory.Exists(completedRun));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public void Prune_ActiveRunWithinKeepLastWindow_StillNotDeletedByAgeRule()
    {
        var workspace = CreateWorkspace();
        try
        {
            var protectedByManifest = CreateRunDirectory(workspace, "d-active-recent-check", ageDays: 30);
            WriteManifest(protectedByManifest, "d-active-recent-check", finishedAt: null);
            var completedOld = CreateRunDirectory(workspace, "d-completed-old", ageDays: 30);
            WriteManifest(completedOld, "d-completed-old", finishedAt: DateTimeOffset.UtcNow.AddDays(-30));

            RunRetention.Prune(workspace, keepLast: 0, maxAgeDays: 1);

            Assert.True(Directory.Exists(protectedByManifest));
            Assert.False(Directory.Exists(completedOld));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static string CreateWorkspace()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"distill-retention-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        return workspace;
    }

    private static string CreateRunDirectory(string workspace, string runId, int ageDays)
    {
        var runDirectory = RunArtifactLayout.GetRunDirectory(workspace, runId);
        Directory.CreateDirectory(runDirectory);
        var timestamp = DateTime.UtcNow.AddDays(-ageDays);
        Directory.SetLastWriteTimeUtc(runDirectory, timestamp);
        return runDirectory;
    }

    private static void WriteManifest(string runDirectory, string runId, DateTimeOffset? finishedAt)
    {
        var manifest = new RunManifest(
            runId,
            DateTimeOffset.UtcNow.AddDays(-1),
            "/repo",
            finishedAt,
            finishedAt is null ? null : VerificationStatus.Pass);

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() }
        });
        File.WriteAllText(RunArtifactLayout.GetRunManifestPath(runDirectory), json);
        Directory.SetLastWriteTimeUtc(runDirectory, DateTime.UtcNow.AddDays(-30));
    }
}
