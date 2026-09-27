using CodeMap.Core.Models;
using CodeMap.Engine;
using CodeMap.Storage;
using Microsoft.Data.Sqlite;

namespace CodeMap.Core.Tests;

public sealed class SymbolsIntersectingPathMatchTests
{
    [Fact]
    public async Task SymbolsIntersecting_SnapshotAndSqlite_UseTheSamePathIdentity()
    {
        var directory = Path.Combine(Path.GetTempPath(), "codemap-intersect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "index.db");
        var symbols = new[]
        {
            Symbol("src", "ProjectA", "A.cs", "SrcApply"),
            Symbol("tests", "ProjectA.Tests", "A.cs", "TestApply"),
            Symbol("main", "App", "Program.cs", "Main"),
            Symbol("other", "Other", "Program.cs", "OtherMain")
        };
        try
        {
            await SeedAsync(databasePath, symbols);
            var snapshot = new CodeMapQueryService(new CodeMapSnapshot
            {
                Files = [],
                Symbols = symbols,
                Edges = []
            });
            var connection = await new CodeMapQueryStore(databasePath).OpenReadOnlyConnectionAsync();
            await using var sql = new CodeMapQueryService(connection);

            AssertSame(snapshot, sql, "ProjectA/A.cs", "src");
            AssertSame(snapshot, sql, "projecta/a.cs", "src");
            AssertSame(snapshot, sql, @"ProjectA\A.cs", "src");
            AssertSame(snapshot, sql, "ProjectA.Tests/A.cs", "tests");
            AssertSame(snapshot, sql, "App/Program.cs", "main");
            AssertSame(snapshot, sql, "src/ProjectA/A.cs");
            AssertSame(snapshot, sql, "proof.yml");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SymbolsIntersecting_CaseOnlyIdentities_MatchNeitherStore()
    {
        var directory = Path.Combine(Path.GetTempPath(), "codemap-intersect-case-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "index.db");
        var symbols = new[]
        {
            Symbol("upper", "Lib", "A.cs", "Upper"),
            Symbol("lower", "lib", "A.cs", "Lower")
        };
        try
        {
            await SeedAsync(databasePath, symbols);
            var snapshot = new CodeMapQueryService(new CodeMapSnapshot
            {
                Files = [],
                Symbols = symbols,
                Edges = []
            });
            var connection = await new CodeMapQueryStore(databasePath).OpenReadOnlyConnectionAsync();
            await using var sql = new CodeMapQueryService(connection);

            AssertSame(snapshot, sql, "Lib/A.cs");
            AssertSame(snapshot, sql, "lib/a.cs");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertSame(CodeMapQueryService snapshot, CodeMapQueryService sql, string path, params string[] ids)
    {
        var fromSnapshot = snapshot.SymbolsIntersecting(path, 1, 40).Select(symbol => symbol.Id).ToArray();
        var fromSql = sql.SymbolsIntersecting(path, 1, 40).Select(symbol => symbol.Id).ToArray();
        Assert.Equal(ids, fromSnapshot);
        Assert.Equal(fromSnapshot, fromSql);
    }

    private static IndexedSymbol Symbol(string id, string project, string relativePath, string name)
        => new(id, project, "file-" + id, relativePath, NodeKind.Method, name, name, null, 10, 20, "public", "csharp");

    private static async Task SeedAsync(string databasePath, IReadOnlyList<IndexedSymbol> symbols)
    {
        if (File.Exists(databasePath))
            File.Delete(databasePath);

        await new SqliteCodeMapStore(databasePath).InitializeAsync(CancellationToken.None);
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite
        }.ToString());
        await connection.OpenAsync();
        foreach (var symbol in symbols)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO files(id, project, relative_path, language, content_hash, mtime_utc, size, indexed_at)
                VALUES ($fileId, $project, $path, 'csharp', 'hash', '2026-01-01T00:00:00.0000000Z', 0, '2026-01-01T00:00:00.0000000Z');
                INSERT INTO symbols(id, file_id, kind, name, qualified_name, signature, start_line, end_line, visibility, language)
                VALUES ($id, $fileId, 'Method', $name, $name, NULL, $start, $end, 'public', 'csharp');
                """;
            command.Parameters.AddWithValue("$fileId", symbol.FileId);
            command.Parameters.AddWithValue("$project", symbol.Project);
            command.Parameters.AddWithValue("$path", symbol.RelativePath);
            command.Parameters.AddWithValue("$id", symbol.Id);
            command.Parameters.AddWithValue("$name", symbol.Name);
            command.Parameters.AddWithValue("$start", symbol.StartLine);
            command.Parameters.AddWithValue("$end", symbol.EndLine);
            await command.ExecuteNonQueryAsync();
        }

        CodeMapEngineBootstrap.EnsureInitialized();
        await using var metadata = connection.CreateCommand();
        metadata.CommandText = """
            INSERT INTO metadata(key, value)
            VALUES ('schema_version', $schemaVersion), ('analyzer_version_csharp', $csharpAnalyzerVersion);
            """;
        metadata.Parameters.AddWithValue("$schemaVersion", SqliteCodeMapStore.SchemaVersion);
        metadata.Parameters.AddWithValue("$csharpAnalyzerVersion", SqliteCodeMapStore.CurrentAnalyzerVersions["csharp"]);
        await metadata.ExecuteNonQueryAsync();
    }
}
