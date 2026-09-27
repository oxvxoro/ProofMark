namespace Distill.Core.Runs;

public static class RunArtifactLayout
{
    public const string DistillRootFolder = ".distill";
    public const string RunsFolder = "runs";
    public const string CacheFolder = "cache";

    public static string GetRunsRoot(string workspaceRoot)
        => Path.Combine(workspaceRoot, DistillRootFolder, RunsFolder);

    public static string GetCacheRoot(string workspaceRoot)
        => Path.Combine(workspaceRoot, DistillRootFolder, CacheFolder);

    public static string GetCacheEntryDirectory(string workspaceRoot, string cacheKey)
        => Path.Combine(GetCacheRoot(workspaceRoot), cacheKey);

    public static string GetRunDirectory(string workspaceRoot, string runId)
        => Path.Combine(GetRunsRoot(workspaceRoot), runId);

    public static string GetRunManifestPath(string runDirectory)
        => Path.Combine(runDirectory, "run.json");

    public static string GetCheckDirectory(string runDirectory, string checkId)
    {
        if (string.Equals(checkId, "build", StringComparison.OrdinalIgnoreCase))
        {
            return GetBuildDirectory(runDirectory);
        }

        if (string.Equals(checkId, "unit", StringComparison.OrdinalIgnoreCase))
        {
            return GetUnitDirectory(runDirectory);
        }

        return Path.Combine(runDirectory, ToSafeDirectoryName(checkId));
    }

    public static string GetBuildDirectory(string runDirectory)
        => Path.Combine(runDirectory, "build");

    public static string GetBuildBinlogPath(string runDirectory)
        => Path.Combine(GetBuildDirectory(runDirectory), "build.binlog");

    public static string GetBuildStdoutPath(string runDirectory)
        => Path.Combine(GetBuildDirectory(runDirectory), "stdout.log");

    public static string GetBuildStderrPath(string runDirectory)
        => Path.Combine(GetBuildDirectory(runDirectory), "stderr.log");

    public static string GetBuildDiagnosticsPath(string runDirectory)
        => Path.Combine(GetBuildDirectory(runDirectory), "diagnostics.json");

    public static string GetUnitDirectory(string runDirectory)
        => Path.Combine(runDirectory, "unit");

    private static string ToSafeDirectoryName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        invalid.Add(':');
        invalid.Add(Path.DirectorySeparatorChar);
        invalid.Add(Path.AltDirectorySeparatorChar);
        var safe = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return safe is "" or "." or ".." ? "_" : safe;
    }

    public static string GetUnitEventsPath(string runDirectory)
        => Path.Combine(GetUnitDirectory(runDirectory), "tests.events.jsonl");

    public static string GetUnitTrxPath(string runDirectory)
        => Path.Combine(GetUnitDirectory(runDirectory), "fallback.trx");

    public static string GetUnitStdoutPath(string runDirectory)
        => Path.Combine(GetUnitDirectory(runDirectory), "stdout.log");

    public static string GetUnitStderrPath(string runDirectory)
        => Path.Combine(GetUnitDirectory(runDirectory), "stderr.log");

    public static string GetUnitDiagnosticsPath(string runDirectory)
        => Path.Combine(GetUnitDirectory(runDirectory), "diagnostics.json");

    public static string GetGitDirectory(string runDirectory)
        => Path.Combine(runDirectory, "git");

    public static string GetGitStatusPath(string runDirectory)
        => Path.Combine(GetGitDirectory(runDirectory), "status.txt");

    public static string GetGitDiffPath(string runDirectory)
        => Path.Combine(GetGitDirectory(runDirectory), "diff.patch");

    public static string GetConfigSnapshotPath(string runDirectory)
        => Path.Combine(runDirectory, "config.snapshot.yml");

    public static string GetNormalizedPath(string runDirectory)
        => Path.Combine(runDirectory, "normalized.json");

    public static string GetFailurePackPath(string runDirectory)
        => Path.Combine(runDirectory, "failure-pack.txt");

    public static string GetStatePath(string workspaceRoot)
        => Path.Combine(workspaceRoot, DistillRootFolder, "state.json");

    public static void EnsureRunDirectories(string runDirectory)
    {
        Directory.CreateDirectory(GetBuildDirectory(runDirectory));
        Directory.CreateDirectory(GetUnitDirectory(runDirectory));
        Directory.CreateDirectory(GetGitDirectory(runDirectory));
    }
}
