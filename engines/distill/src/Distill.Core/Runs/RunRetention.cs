using System.Text.Json;

namespace Distill.Core.Runs;

public static class RunRetention
{
    public static void Prune(
        string workspaceRoot,
        int keepLast = 20,
        int maxAgeDays = 7,
        string? protectedRunId = null)
    {
        var runsRoot = RunArtifactLayout.GetRunsRoot(workspaceRoot);
        if (!Directory.Exists(runsRoot))
        {
            return;
        }

        var cutoff = DateTime.UtcNow.AddDays(-maxAgeDays);
        var directories = Directory
            .EnumerateDirectories(runsRoot)
            .Select(path => new DirectoryInfo(path))
            .OrderByDescending(directory => directory.LastWriteTimeUtc)
            .ToList();

        for (var index = 0; index < directories.Count; index++)
        {
            var directory = directories[index];
            if (index < keepLast
                || directory.LastWriteTimeUtc >= cutoff
                || string.Equals(directory.Name, protectedRunId, StringComparison.OrdinalIgnoreCase)
                || IsActiveOrIndeterminate(directory.FullName))
            {
                continue;
            }

            try
            {
                directory.Delete(recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static bool IsActiveOrIndeterminate(string runDirectory)
    {
        var manifestPath = RunArtifactLayout.GetRunManifestPath(runDirectory);
        if (!File.Exists(manifestPath))
        {
            // 매니페스트가 전혀 없다. 매니페스트 추적 이전의 레거시 실행이거나, 초기
            // 매니페스트 쓰기가 한 번도 되지 않은 실행이다. 기존의 기간/개수 규칙이 이미
            // 이 디렉터리를 다스린다(ReliabilityTests.RunRetention_* 참고). 따라서
            // 활성으로 취급하지 않고 정리 대상에 남긴다.
            return false;
        }

        try
        {
            var json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<RunManifest>(json, new JsonSerializerOptions
            {
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            });

            // 매니페스트는 있으나 형식이 잘못되었거나 FinishedAt == null인 상태
            // (초기 매니페스트는 썼으나 최종 매니페스트는 게시되지 않음)는
            // 활성 또는 중단된 실행을 뜻하며, 보수적으로 보존한다.
            return manifest is null || manifest.FinishedAt is null;
        }
        catch (JsonException)
        {
            return true;
        }
        catch (IOException)
        {
            return true;
        }
    }
}
