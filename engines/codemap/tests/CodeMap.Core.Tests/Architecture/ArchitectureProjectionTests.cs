using CodeMap.Core.Models;
using CodeMap.Engine;
using CodeMap.Storage;
using Microsoft.Data.Sqlite;

namespace CodeMap.Core.Tests;

/// <summary>
/// 아키텍처 프로젝션 로더는 전체 그래프를 읽고 나중에 거르는 것과
/// 같은 검사 결과를 내야 한다. 구조 엣지와 (선택적으로) 휴리스틱
/// 엣지를 SQL에서 통째로 버린다.
/// </summary>
public sealed class ArchitectureProjectionTests
{
    private static readonly ArchitectureRules Rules = new(
        Layers: ["Ui", "Domain"],
        Forbid: [["Ui", "Domain"]],
        MaxFanIn: 1,
        MaxFanOut: 1);

    private static readonly IReadOnlyDictionary<string, string[]> ProjectReferences =
        new Dictionary<string, string[]>(StringComparer.Ordinal);

    [Fact]
    public async Task Projection_ExcludingHeuristic_MatchesFullGraphWithProofSideFilter()
    {
        var databasePath = await SeedDatabaseAsync();
        try
        {
            var store = new CodeMapQueryStore(databasePath);
            var full = await store.LoadAsync();

            var fullFiltered = new CodeMapSnapshot
            {
                Files = full.Files,
                Symbols = full.Symbols,
                Edges = full.Edges.Where(edge => edge.ResolutionKind != EdgeResolutionKind.Heuristic).ToArray()
            };
            var expected = ArchitectureChecker.Check(fullFiltered, ProjectReferences, Rules);

            var projection = await store.LoadArchitectureProjectionAsync(excludeHeuristic: true);
            var actual = ArchitectureChecker.Check(projection, ProjectReferences, Rules);

            Assert.NotEmpty(expected);
            Assert.Equal(Describe(expected), Describe(actual));

            // 픽스처 건전성: 프로젝션이 공허하게 비어서는 안 된다.
            Assert.Contains(expected, item => item.Kind == "layer" && item.Source == "ui-caller" && item.Target == "domain-target");
            Assert.Contains(expected, item => item.Kind == "fan-in" && item.Source == "hub");
            Assert.Contains(expected, item => item.Kind == "fan-out" && item.Source == "fanout-src");
            Assert.Contains(expected, item => item.Kind == "orphan" && item.Source == "orphan-pub");
            Assert.DoesNotContain(expected, item => item.Kind == "layer" && item.Target == "domain-guess");

            Assert.Empty(projection.Files);
            Assert.DoesNotContain(projection.Edges, edge => edge.Kind is EdgeKind.Contains or EdgeKind.Defines);
            Assert.DoesNotContain(projection.Edges, edge => edge.ResolutionKind == EdgeResolutionKind.Heuristic);
        }
        finally
        {
            Cleanup(databasePath);
        }
    }

    [Fact]
    public async Task Projection_IncludingHeuristic_MatchesFullGraphIncludingHeuristic()
    {
        var databasePath = await SeedDatabaseAsync();
        try
        {
            var store = new CodeMapQueryStore(databasePath);
            var full = await store.LoadAsync();
            var expected = ArchitectureChecker.Check(full, ProjectReferences, Rules);

            var projection = await store.LoadArchitectureProjectionAsync(excludeHeuristic: false);
            var actual = ArchitectureChecker.Check(projection, ProjectReferences, Rules);

            Assert.Equal(Describe(expected), Describe(actual));
            Assert.Contains(expected, item => item.Kind == "layer" && item.Target == "domain-guess");
        }
        finally
        {
            Cleanup(databasePath);
        }
    }

    private static IReadOnlyList<string> Describe(IReadOnlyList<ArchitectureViolation> violations) =>
        violations.Select(item => $"{item.Kind}|{item.Source}|{item.Target}|{item.Message}").ToArray();

    private static void Cleanup(string databasePath)
    {
        SqliteConnection.ClearAllPools();
        var root = Path.GetDirectoryName(databasePath)!;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<string> SeedDatabaseAsync()
    {
        CodeMapEngineBootstrap.EnsureInitialized();
        var root = Path.Combine(Path.GetTempPath(), "codemap-arch-projection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "index.db");

        await new SqliteCodeMapStore(databasePath).InitializeAsync(CancellationToken.None);
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite
        }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO files(id, project, relative_path, language, content_hash, mtime_utc, size, indexed_at)
            VALUES
                ('ui-file', 'UiProject', 'Ui/View.cs', 'csharp', 'hash', '2026-01-01T00:00:00.0000000Z', 0, '2026-01-01T00:00:00.0000000Z'),
                ('domain-file', 'DomainProject', 'Domain/Service.cs', 'csharp', 'hash', '2026-01-01T00:00:00.0000000Z', 0, '2026-01-01T00:00:00.0000000Z');

            INSERT INTO symbols(id, file_id, kind, name, qualified_name, signature, start_line, end_line, visibility, language)
            VALUES
                ('ui-caller', 'ui-file', 'Method', 'Caller', 'Ui.Caller', NULL, 1, 1, 'public', 'csharp'),
                ('domain-target', 'domain-file', 'Method', 'Target', 'Domain.Target', NULL, 1, 1, 'public', 'csharp'),
                ('domain-guess', 'domain-file', 'Method', 'Guess', 'Domain.Guess', NULL, 2, 2, 'public', 'csharp'),
                ('hub', 'domain-file', 'Method', 'Hub', 'Domain.Hub', NULL, 3, 3, 'public', 'csharp'),
                ('hub-caller-a', 'ui-file', 'Method', 'HubCallerA', 'Ui.HubCallerA', NULL, 2, 2, 'public', 'csharp'),
                ('hub-caller-b', 'ui-file', 'Method', 'HubCallerB', 'Ui.HubCallerB', NULL, 3, 3, 'public', 'csharp'),
                ('fanout-src', 'ui-file', 'Method', 'Fanout', 'Ui.Fanout', NULL, 4, 4, 'public', 'csharp'),
                ('fanout-dst-a', 'domain-file', 'Method', 'FanoutA', 'Domain.FanoutA', NULL, 4, 4, 'public', 'csharp'),
                ('fanout-dst-b', 'domain-file', 'Method', 'FanoutB', 'Domain.FanoutB', NULL, 5, 5, 'public', 'csharp'),
                ('orphan-pub', 'ui-file', 'Method', 'Orphan', 'Ui.Orphan', NULL, 5, 5, 'public', 'csharp');

            INSERT INTO edges(source_id, target_id, kind, source_file_id, line, start_column, end_line, end_column, resolution_kind, confidence)
            VALUES
                ('ui-caller', 'domain-target', 'UsesType', 'ui-file', 1, NULL, NULL, NULL, 'semantic', NULL),
                ('ui-caller', 'domain-guess', 'UsesType', 'ui-file', 2, NULL, NULL, NULL, 'heuristic', NULL),
                ('hub-caller-a', 'hub', 'Calls', 'ui-file', 2, NULL, NULL, NULL, 'semantic', NULL),
                ('hub-caller-b', 'hub', 'Calls', 'ui-file', 3, NULL, NULL, NULL, 'semantic', NULL),
                ('fanout-src', 'fanout-dst-a', 'Calls', 'ui-file', 4, NULL, NULL, NULL, 'semantic', NULL),
                ('fanout-src', 'fanout-dst-b', 'Calls', 'ui-file', 5, NULL, NULL, NULL, 'semantic', NULL),
                ('ui-caller', 'orphan-pub', 'Contains', 'ui-file', 6, NULL, NULL, NULL, 'semantic', NULL),
                ('domain-target', 'hub', 'Imports', 'domain-file', 7, NULL, NULL, NULL, 'semantic', NULL);

            INSERT INTO metadata(key, value)
            VALUES ('schema_version', $schemaVersion), ('analyzer_version_csharp', $csharpAnalyzerVersion);
            """;
        command.Parameters.AddWithValue("$schemaVersion", SqliteCodeMapStore.SchemaVersion);
        command.Parameters.AddWithValue("$csharpAnalyzerVersion", SqliteCodeMapStore.CurrentAnalyzerVersions["csharp"]);
        await command.ExecuteNonQueryAsync();

        return databasePath;
    }
}
