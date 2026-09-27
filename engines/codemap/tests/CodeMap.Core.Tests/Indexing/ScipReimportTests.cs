using CodeMap.Scip.Protocol;
using CodeMap.Storage;
using Google.Protobuf;
using Microsoft.Data.Sqlite;
using ProtoIndex = CodeMap.Scip.Protocol.Index;

namespace CodeMap.Core.Tests;

/// <summary>
/// .codemap/scip-providers.json에 선언된 SCIP 산출물은 해시가 바뀌면 다시 읽힌다.
/// 선언이 없거나 산출물 파일이 없으면 스테일 예외가 그대로 난다.
/// </summary>
[Collection("MsBuild")]
public sealed class ScipReimportTests
{
    [Fact]
    public async Task Update_DeclaredArtifactHashChange_ReimportsInsteadOfThrowing()
    {
        var root = CreateRoot();
        try
        {
            await WriteScipAsync(root, "run");
            await IncrementalCodeMapIndexer.CreateDefault().IndexAsync(root, force: true);
            await new ScipImportService().ImportAsync(root, Path.Combine(root, "app.scip"), new ScipImportOptions("python"));
            await WriteManifestAsync(root, "app.scip");

            await WriteScipAsync(root, "next");
            await IncrementalCodeMapIndexer.CreateDefault().UpdateAsync(root);

            var query = await QueryAsync(root);
            Assert.DoesNotContain(query.Find("run", 10), item => item.Id.StartsWith("scip://python/", StringComparison.Ordinal));
            Assert.Contains(query.Find("next", 10), item => item.Id.StartsWith("scip://python/", StringComparison.Ordinal));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task Update_DeclaredArtifactMissing_StillThrowsStale()
    {
        var root = CreateRoot();
        try
        {
            await WriteScipAsync(root, "run");
            await IncrementalCodeMapIndexer.CreateDefault().IndexAsync(root, force: true);
            await new ScipImportService().ImportAsync(root, Path.Combine(root, "app.scip"), new ScipImportOptions("python"));
            await WriteManifestAsync(root, "missing.scip");

            await File.AppendAllTextAsync(Path.Combine(root, "app.py"), "# changed\n");
            await Assert.ThrowsAsync<InvalidOperationException>(() => IncrementalCodeMapIndexer.CreateDefault().UpdateAsync(root));

            var query = await QueryAsync(root);
            Assert.Contains(query.Find("run", 10), item => item.Id.StartsWith("scip://python/", StringComparison.Ordinal));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task Index_ImportsDeclaredProviders_AndForceReimportsStaleDeclaredArtifact()
    {
        var root = CreateRoot();
        try
        {
            await WriteScipAsync(root, "run");
            Directory.CreateDirectory(Path.Combine(root, ".codemap"));
            await WriteManifestAsync(root, "app.scip");

            await IncrementalCodeMapIndexer.CreateDefault().IndexAsync(root);
            Assert.Contains((await QueryAsync(root)).Find("run", 10), item => item.Id.StartsWith("scip://python/", StringComparison.Ordinal));

            await WriteScipAsync(root, "next");
            await IncrementalCodeMapIndexer.CreateDefault().IndexAsync(root, force: true);
            var query = await QueryAsync(root);
            Assert.DoesNotContain(query.Find("run", 10), item => item.Id.StartsWith("scip://python/", StringComparison.Ordinal));
            Assert.Contains(query.Find("next", 10), item => item.Id.StartsWith("scip://python/", StringComparison.Ordinal));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task SolutionReconcile_KeepsUndeclaredScipProject()
    {
        var root = CreateRoot();
        try
        {
            var fixture = Path.Combine(
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")),
                "tests", "Fixtures", "MultiProject");
            foreach (var file in Directory.EnumerateFiles(fixture, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(root, Path.GetRelativePath(fixture, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }

            await WriteScipAsync(root, "run");
            await IncrementalCodeMapIndexer.CreateDefault().IndexAsync(root, force: true);
            await new ScipImportService().ImportAsync(root, Path.Combine(root, "app.scip"), new ScipImportOptions("python"));

            await File.AppendAllTextAsync(Path.Combine(root, "MultiProject.slnx"), Environment.NewLine);
            await IncrementalCodeMapIndexer.CreateDefault().UpdateAsync(root);

            Assert.Contains((await QueryAsync(root)).Find("run", 10), item => item.Id.StartsWith("scip://python/", StringComparison.Ordinal));
        }
        finally
        {
            Cleanup(root);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "codemap-scip-reimport-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        return root;
    }

    // app.py와 그것을 가리키는 app.scip를 함께 쓴다. 이름이 바뀌면 산출물 해시도 바뀐다.
    private static async Task WriteScipAsync(string root, string functionName)
    {
        var symbol = $"scip-python python pkg 1.0 {functionName}().";
        await File.WriteAllTextAsync(Path.Combine(root, "app.py"), $"def {functionName}():\n    return 1\n");
        var index = new ProtoIndex
        {
            Documents =
            {
                new Document
                {
                    RelativePath = "app.py",
                    Language = "python",
                    Symbols = { new SymbolInformation { Symbol = symbol, DisplayName = functionName, Kind = 17 } },
                    Occurrences =
                    {
                        new Occurrence
                        {
                            Symbol = symbol,
                            SymbolRoles = 1,
                            SingleLineRange = new SingleLineRange { Line = 0, StartCharacter = 4, EndCharacter = 4 + functionName.Length }
                        }
                    }
                }
            }
        };
        await File.WriteAllBytesAsync(Path.Combine(root, "app.scip"), index.ToByteArray());
    }

    private static Task WriteManifestAsync(string root, string artifact)
        => File.WriteAllTextAsync(
            Path.Combine(root, ".codemap", "scip-providers.json"),
            $$"""{"providers":[{"name":"python","artifact":"{{artifact}}"}]}""");

    private static async Task<CodeMapQueryService> QueryAsync(string root)
        => new(await new CodeMapQueryStore(Path.Combine(root, ".codemap", "index.db")).LoadAsync());

    private static void Cleanup(string root)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
