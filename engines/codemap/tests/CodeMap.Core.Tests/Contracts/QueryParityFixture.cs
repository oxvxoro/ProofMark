using CodeMap.Storage;

namespace CodeMap.Core.Tests.Contracts;

internal sealed class QueryParityFixture : IAsyncDisposable
{
    private QueryParityFixture(
        string workingDirectory,
        CodeMapSnapshot snapshot,
        CodeMapQueryService snapshotService,
        CodeMapQueryService sqlService)
    {
        WorkingDirectory = workingDirectory;
        Snapshot = snapshot;
        SnapshotService = snapshotService;
        SqlService = sqlService;
    }

    public string WorkingDirectory { get; }

    public CodeMapSnapshot Snapshot { get; }

    public CodeMapQueryService SnapshotService { get; }

    public CodeMapQueryService SqlService { get; }

    public static async Task<QueryParityFixture> CreateAsync(string fixtureName)
    {
        var workingDirectory = await CodeMap.Core.Tests.IndexedFixtureCache.GetAsync(fixtureName);
        var store = new CodeMapQueryStore(Path.Combine(workingDirectory, ".codemap", "index.db"));
        var snapshot = await store.LoadAsync();
        var sqlService = new CodeMapQueryService(await store.OpenReadOnlyConnectionAsync());
        return new QueryParityFixture(workingDirectory, snapshot, new CodeMapQueryService(snapshot), sqlService);
    }

    public async ValueTask DisposeAsync()
    {
        await SnapshotService.DisposeAsync();
        await SqlService.DisposeAsync();
    }
}
