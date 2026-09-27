using Microsoft.Data.Sqlite;
using CodeMap.Core.Models;

namespace CodeMap.Storage;

public sealed record CodeMapIndexStatus(
    string DatabasePath,
    string IndexState,
    DateTimeOffset? LastIndexedAtUtc,
    string? SchemaVersion,
    bool SchemaOutdated,
    IReadOnlyDictionary<string, string> AnalyzerVersions,
    bool AnalyzerVersionsOutdated,
    int Symbols,
    int Edges)
{
    public IndexLifecycleState LifecycleState => IndexLifecycleStateParser.Parse(IndexState);
}

public static class CodeMapIndexStatusReader
{
    public static async Task<CodeMapIndexStatus> ReadAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(databasePath);
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared
        }.ToString());
        await connection.OpenAsync(cancellationToken);

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT key, value FROM metadata";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                metadata[reader.GetString(0)] = reader.GetString(1);
        }
        catch (SqliteException)
        {
            // 메타데이터가 도입되기 전에 만든 인덱스는 계속 검사할 수 있다.
        }

        var (symbols, edges) = await ReadCountsAsync(connection, cancellationToken);
        var analyzerVersions = metadata
            .Where(item => item.Key.StartsWith("analyzer_version_", StringComparison.Ordinal))
            .ToDictionary(item => item.Key["analyzer_version_".Length..], item => item.Value, StringComparer.Ordinal);
        var indexedAnalyzerLanguages = await ReadIndexedAnalyzerLanguagesAsync(connection, cancellationToken);
        var expectedAnalyzerVersions = indexedAnalyzerLanguages is null
            ? SqliteCodeMapStore.CurrentAnalyzerVersions
            : SqliteCodeMapStore.CurrentAnalyzerVersions
                .Where(expected => indexedAnalyzerLanguages.Contains(expected.Key))
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var analyzerVersionsOutdated = expectedAnalyzerVersions.Any(expected =>
            !analyzerVersions.TryGetValue(expected.Key, out var actual)
            || !string.Equals(actual, expected.Value, StringComparison.Ordinal));
        var schemaVersion = metadata.GetValueOrDefault("schema_version");
        var lastIndexedAtUtc = metadata.TryGetValue("last_indexed_at_utc", out var indexedAt)
            && DateTimeOffset.TryParse(indexedAt, out var parsedIndexedAt)
            ? (DateTimeOffset?)parsedIndexedAt
            : null;

        return new CodeMapIndexStatus(
            fullPath,
            metadata.GetValueOrDefault("index_state")
                ?? (schemaVersion is null ? "not_indexed" : "ready"),
            lastIndexedAtUtc,
            schemaVersion,
            !string.Equals(schemaVersion, SqliteCodeMapStore.SchemaVersion, StringComparison.Ordinal),
            analyzerVersions,
            analyzerVersionsOutdated,
            symbols,
            edges);
    }

    private static async Task<HashSet<string>?> ReadIndexedAnalyzerLanguagesAsync(
        SqliteConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT DISTINCT language FROM files";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var languages = new HashSet<string>(StringComparer.Ordinal);
            while (await reader.ReadAsync(cancellationToken))
            {
                var analyzerLanguage = SqliteCodeMapStore.AnalyzerLanguageForFile(reader.GetString(0));
                if (analyzerLanguage is not null)
                    languages.Add(analyzerLanguage);
            }
            return languages;
        }
        catch (SqliteException)
        {
            // 레거시 인덱스에는 files 테이블이 없을 수 있다. 그 경우 알려진 메타데이터를 모두 비교한다.
            return null;
        }
    }

    private static async Task<(int Symbols, int Edges)> ReadCountsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT (SELECT COUNT(*) FROM symbols), (SELECT COUNT(*) FROM edges)";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
                return (reader.GetInt32(0), reader.GetInt32(1));
        }
        catch (SqliteException)
        {
            // 부분적으로 만들어졌거나 레거시인 인덱스도 메타데이터를 보고할 수 있다.
        }

        return (0, 0);
    }
}
