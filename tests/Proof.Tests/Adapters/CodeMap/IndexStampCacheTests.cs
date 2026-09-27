using CodeMap.Storage;
using Proof.Adapters.CodeMap;
using Proof.Core;

namespace Proof.Tests;

public sealed class IndexStampCacheTests
{
    [Fact]
    public void ShouldSkipIndexUpdate_Lifecycle()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-stamp-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".codemap"));
            File.WriteAllText(Path.Combine(root, ".codemap", "index.db"), "db");
            var request = new ChangeRequest(root, "base", "head", [], SourceDigest: "d1");

            Assert.False(CodeMapChangeImpactProvider.ShouldSkipIndexUpdate(root, request, root));

            CodeMapChangeImpactProvider.WriteIndexStamp(root, request, root);

            Assert.True(CodeMapChangeImpactProvider.ShouldSkipIndexUpdate(root, request, root));
            Assert.False(CodeMapChangeImpactProvider.ShouldSkipIndexUpdate(root, request with { SourceDigest = "d2" }, root));
            Assert.False(CodeMapChangeImpactProvider.ShouldSkipIndexUpdate(root, request with { IsDirty = true }, root));
            Assert.False(CodeMapChangeImpactProvider.ShouldSkipIndexUpdate(
                root, new ChangeRequest(root, "base", "head", []), root));

            // 건드린 db는 스탬프를 무효화한다.
            File.AppendAllText(Path.Combine(root, ".codemap", "index.db"), "changed");
            Assert.False(CodeMapChangeImpactProvider.ShouldSkipIndexUpdate(root, request, root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IndexStamp_RoundTrips()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-stamp-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".codemap"));
            File.WriteAllText(Path.Combine(root, ".codemap", "index.db"), "db");
            var request = new ChangeRequest(root, "base", "head", [], SourceDigest: "d1");

            CodeMapChangeImpactProvider.WriteIndexStamp(root, request, root);
            var stamp = CodeMapChangeImpactProvider.TryReadIndexStamp(
                CodeMapChangeImpactProvider.IndexStampPath(root));

            Assert.NotNull(stamp);
            Assert.Equal("d1", stamp!.SourceDigest);
            Assert.Equal(Path.GetFullPath(root), stamp.IndexInput);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

/// <summary>
/// 인덱스 스탬프 건너뛰기의 통합 테스트: 일치하는 다이제스트는 인덱서
/// 팩토리를 호출하면 안 되고, 바뀐 다이제스트는 다시 만들어야 한다. simple-service
/// 픽스처에 실제 CodeMap 인덱서를 사용한다. 인덱서는 SqliteConnection.ClearAllPools()를
/// 부르므로, 같은 프로세스에서 인덱싱하는 테스트와 동시에 돌면 스탬프가 깨진다.
/// </summary>
[Collection("CodeMapInProcessIndex")]
public sealed class IndexStampIntegrationTests
{
    [Fact(Timeout = 240_000)]
    public async Task AnalyzeAsync_SameDigest_SkipsIndexer_ChangedDigestRebuilds()
    {
        var root = CreateFixtureCopy();
        try
        {
            var options = new CodeMapAnalysisOptions(
                root,
                Path.Combine(root, "SimpleService.sln"),
                new ImpactAnalysisSettings());

            var firstFactoryCalls = 0;
            var first = new CodeMapChangeImpactProvider(options, null, () =>
            {
                firstFactoryCalls++;
                return IncrementalCodeMapIndexer.CreateDefault();
            });
            await first.AnalyzeAsync(
                new ChangeRequest(root, "base", "head", [], SourceDigest: "d1", FileDeltas: []),
                CancellationToken.None);
            Assert.Equal(1, firstFactoryCalls);

            var skipFactoryCalls = 0;
            var skip = new CodeMapChangeImpactProvider(options, null, () =>
            {
                skipFactoryCalls++;
                return IncrementalCodeMapIndexer.CreateDefault();
            });
            await skip.AnalyzeAsync(
                new ChangeRequest(root, "base", "head", [], SourceDigest: "d1", FileDeltas: []),
                CancellationToken.None);
            Assert.Equal(0, skipFactoryCalls);

            var changedFactoryCalls = 0;
            var changed = new CodeMapChangeImpactProvider(options, null, () =>
            {
                changedFactoryCalls++;
                return IncrementalCodeMapIndexer.CreateDefault();
            });
            await changed.AnalyzeAsync(
                new ChangeRequest(root, "base", "head", [], SourceDigest: "d2", FileDeltas: []),
                CancellationToken.None);
            Assert.Equal(1, changedFactoryCalls);
        }
        finally
        {
            Cleanup(root);
        }
    }

    private static string CreateFixtureCopy()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-indexcache-" + Guid.NewGuid().ToString("N"));
        var source = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "fixtures", "proof", "simple-service"));
        Directory.CreateDirectory(root);
        foreach (var file in Directory.EnumerateFiles(source, "*.*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }

        return root;
    }

    private static void Cleanup(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}