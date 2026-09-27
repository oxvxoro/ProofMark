using CodeMap.Storage;
using Microsoft.Data.Sqlite;

namespace CodeMap.Core.Tests;

[Collection("MsBuild")]
public sealed class IndexedFixtureCacheTests
{
    [Fact]
    public async Task GetAsync_IndexesEachFixtureOnce()
    {
        var first = await IndexedFixtureCache.GetAsync("MultiProject");
        var buildsAfterFirst = IndexedFixtureCache.BuildCount("MultiProject");
        var second = await IndexedFixtureCache.GetAsync("MultiProject");

        Assert.Equal(first, second);
        Assert.Equal(1, buildsAfterFirst);
        Assert.Equal(buildsAfterFirst, IndexedFixtureCache.BuildCount("MultiProject"));
        Assert.True(File.Exists(Path.Combine(first, ".codemap", "index.db")));
    }

    [Fact]
    public async Task CopyAsync_UnmodifiedClone_UpdateIsNoOp()
    {
        var workingDirectory = await IndexedFixtureCache.CopyAsync("MultiProject");
        try
        {
            var update = await IncrementalCodeMapIndexer.CreateDefault().UpdateAsync(workingDirectory, CancellationToken.None);
            Assert.Equal(0, update.Added);
            Assert.Equal(0, update.Updated);
            Assert.Equal(0, update.Removed);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(workingDirectory))
                Directory.Delete(workingDirectory, recursive: true);
        }
    }
}
