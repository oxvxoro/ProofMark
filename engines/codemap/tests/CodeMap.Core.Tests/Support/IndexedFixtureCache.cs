using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodeMap.Storage;
using Microsoft.Data.Sqlite;

namespace CodeMap.Core.Tests;

/// <summary>
/// 픽스처마다 Roslyn 인덱스를 프로세스에서 한 번만 만든다.
/// 읽기 전용 테스트는 <see cref="GetAsync"/> 루트를 공유하고,
/// 파일을 고치는 테스트는 <see cref="CopyAsync"/> 복제본을 쓴다.
/// </summary>
internal static class IndexedFixtureCache
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Dictionary<string, string> Roots = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, int> Builds = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> RestoreFirst = new(StringComparer.OrdinalIgnoreCase)
    {
        "AspNetFixture",
        "WpfFixture",
        "CollidingProjectsWithTransitiveReference"
    };

    internal static int BuildCount(string fixtureName)
    {
        lock (Builds)
            return Builds.GetValueOrDefault(fixtureName);
    }

    internal static async Task<string> GetAsync(string fixtureName, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (Roots.TryGetValue(fixtureName, out var existing))
                return existing;

            var destination = Path.Combine(Path.GetTempPath(), "codemap-indexed-" + fixtureName + "-" + Guid.NewGuid().ToString("N"));
            CopyDirectory(ResolveSource(fixtureName), destination, includeCodeMap: false);
            await IncrementalCodeMapIndexer.CreateDefault().IndexAsync(destination, force: true, cancellationToken);
            Checkpoint(destination);
            Builds[fixtureName] = Builds.GetValueOrDefault(fixtureName) + 1;
            Roots[fixtureName] = Path.GetFullPath(destination);
            return Roots[fixtureName];
        }
        finally
        {
            Gate.Release();
        }
    }

    internal static async Task<string> CopyAsync(string fixtureName, CancellationToken cancellationToken = default)
    {
        var template = await GetAsync(fixtureName, cancellationToken);
        SqliteConnection.ClearAllPools();
        var copy = Path.Combine(Path.GetTempPath(), "codemap-indexed-copy-" + fixtureName + "-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(template, copy, includeCodeMap: true);
        RewriteState(copy, template);
        PrimeWal(copy);
        return Path.GetFullPath(copy);
    }

    private static string ResolveSource(string fixtureName)
    {
        if (RestoreFirst.Contains(fixtureName))
            return FixtureRestore.EnsureRestored(fixtureName);

        var solutionRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var source = Path.Combine(solutionRoot, "tests", "Fixtures", fixtureName);
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Fixture '{fixtureName}' was not found at '{source}'.");
        return source;
    }

    private static void CopyDirectory(string source, string destination, bool includeCodeMap)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            if (first is ".git" or "bin")
                continue;
            if (!includeCodeMap && first is ".codemap")
                continue;

            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void Checkpoint(string root)
    {
        // 인덱서가 풀에 연결을 남겨 두면 TRUNCATE가 끝나지 않은 채 WAL이 지워진다.
        SqliteConnection.ClearAllPools();
        var databasePath = Path.Combine(root, ".codemap", "index.db");
        using (var connection = Open(databasePath))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            using var reader = command.ExecuteReader();
            if (!reader.Read() || reader.GetInt32(0) != 0)
                throw new InvalidOperationException($"WAL checkpoint did not complete for '{databasePath}'.");
        }

        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var sidecar = databasePath + suffix;
            if (File.Exists(sidecar))
                File.Delete(sidecar);
        }
    }

    /// <summary>
    /// 읽기 전용 연결과 쓰기가 겹치려면, 인덱서가 쓰던 것과 같은 공유 캐시 연결이 풀에 남아 있어야 한다.
    /// </summary>
    private static void PrimeWal(string root)
    {
        var databasePath = Path.Combine(root, ".codemap", "index.db");
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = WAL;";
        command.ExecuteNonQuery();
        connection.Dispose();
    }

    private static SqliteConnection Open(string databasePath)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite
        }.ToString());
        connection.Open();
        return connection;
    }

    /// <summary>
    /// 템플릿 루트 아래 절대 경로만 복제본으로 옮긴다.
    /// 프로젝트 파일 해시는 절대 경로를 포함하므로 옮긴 경로로 다시 계산한다.
    /// NuGet 캐시의 어셈블리 경로는 템플릿 밖이라 그대로 둔다.
    /// </summary>
    private static void RewriteState(string copyRoot, string templateRoot)
    {
        var statePath = Path.Combine(copyRoot, ".codemap", "state.json");
        var root = JsonNode.Parse(File.ReadAllText(statePath))!.AsObject();
        RelocateAndRehash(root, "resolvedInputPath", "resolvedInputHash", templateRoot, copyRoot, contentHash: true);

        if (root["projects"] is JsonArray projects)
        {
            foreach (var projectNode in projects)
            {
                if (projectNode is not JsonObject project)
                    continue;

                var kind = project["providerKind"]?.GetValue<string>();
                RelocateAndRehash(project, "projectPath", "projectFileHash", templateRoot, copyRoot, contentHash: true);
                RelocateAndRehash(
                    project,
                    "providerInputPath",
                    "providerInputHash",
                    templateRoot,
                    copyRoot,
                    contentHash: !string.Equals(kind, "scip", StringComparison.Ordinal));

                if (project["externalAssemblies"] is not JsonArray assemblies)
                    continue;

                foreach (var assemblyNode in assemblies)
                {
                    if (assemblyNode is JsonObject assembly)
                        RelocateAndRehash(assembly, "assemblyPath", "assemblyHash", templateRoot, copyRoot, contentHash: false);
                }
            }
        }

        File.WriteAllText(statePath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void RelocateAndRehash(
        JsonObject owner,
        string pathProperty,
        string hashProperty,
        string templateRoot,
        string copyRoot,
        bool contentHash)
    {
        if (owner[pathProperty] is not JsonValue pathValue || pathValue.GetValueKind() != JsonValueKind.String)
            return;

        var current = pathValue.GetValue<string>();
        var relocated = Relocate(current, templateRoot, copyRoot);
        if (!string.Equals(relocated, current, StringComparison.Ordinal))
            owner[pathProperty] = relocated;

        if (owner[hashProperty] is null || owner[hashProperty]!.GetValueKind() == JsonValueKind.Null)
            return;
        if (!File.Exists(relocated))
            return;

        owner[hashProperty] = contentHash
            ? SqliteCodeMapStore.ComputeContentHash(relocated, File.ReadAllText(relocated))
            : Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(relocated))).ToLowerInvariant();
    }

    private static string Relocate(string path, string templateRoot, string copyRoot)
    {
        var template = TrimSeparator(Path.GetFullPath(templateRoot));
        var copy = TrimSeparator(Path.GetFullPath(copyRoot));
        var full = TrimSeparator(Path.GetFullPath(path));
        if (full.Equals(template, StringComparison.OrdinalIgnoreCase))
            return copy;
        var prefix = template + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return path;
        return copy + full[template.Length..];
    }

    private static string TrimSeparator(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
