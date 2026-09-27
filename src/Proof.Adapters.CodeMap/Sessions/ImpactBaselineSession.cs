using CodeMap.Core.Contracts;
using CodeMap.CSharp.Analysis;
using Proof.Core;

namespace Proof.Adapters.CodeMap.Sessions;

internal sealed record SurfaceBaseline(
    IReadOnlyDictionary<string, IReadOnlyList<PublicSurfaceEntry>> Entries,
    IReadOnlyDictionary<string, string?> Fingerprints);

internal static class ImpactBaselineSession
{
    internal static async Task<SurfaceBaseline> LoadAsync(
        CodeMapAnalysisOptions analysisOptions,
        string workspaceRoot,
        ChangeRequest request,
        IReadOnlyList<ChangedSymbolRef> changedSymbols,
        IReadOnlyDictionary<string, IReadOnlyList<PublicSurfaceEntry>>? currentEntries,
        CancellationToken cancellationToken)
    {
        var emptyEntries = new Dictionary<string, IReadOnlyList<PublicSurfaceEntry>>(StringComparer.Ordinal);
        IReadOnlyDictionary<string, IReadOnlyList<PublicSurfaceEntry>> baseline = emptyEntries;
        var baselineLoaded = false;

        if (PublicSurfaceSnapshotStore.IsResolvableRevision(request.BaseRevision))
        {
            var cachedDatabaseCorrupt = false;
            var snapshot = PublicSurfaceSnapshotStore.TryLoad(workspaceRoot, request.BaseRevision);
            if (snapshot is not null)
            {
                baseline = snapshot;
                baselineLoaded = true;
            }
            else
            {
                var cachedDatabase = BaseRevisionIndex.GetCachedDatabasePath(workspaceRoot, request.BaseRevision);
                if (File.Exists(cachedDatabase))
                {
                    var fromIndex = await CaptureEntriesFromDatabaseAsync(cachedDatabase, cancellationToken).ConfigureAwait(false);
                    if (fromIndex is not null)
                    {
                        baseline = fromIndex;
                        baselineLoaded = true;
                        PublicSurfaceSnapshotStore.TrySave(workspaceRoot, request.BaseRevision, baseline);
                    }
                    else
                    {
                        cachedDatabaseCorrupt = true;
                    }
                }
            }

            if ((!baselineLoaded || cachedDatabaseCorrupt)
                && analysisOptions.IndexBaseRevision
                && changedSymbols.Any(symbol => symbol.IsPublic))
            {
                var database = await BaseRevisionIndex.EnsureBaseIndexDatabaseAsync(
                    workspaceRoot,
                    request.BaseRevision,
                    cancellationToken,
                    forceRebuild: cachedDatabaseCorrupt).ConfigureAwait(false);
                if (database is not null)
                {
                    var fromWorktree = await CaptureEntriesFromDatabaseAsync(database, cancellationToken).ConfigureAwait(false);
                    if (fromWorktree is not null)
                    {
                        baseline = fromWorktree;
                        baselineLoaded = true;
                        PublicSurfaceSnapshotStore.TrySave(workspaceRoot, request.BaseRevision, baseline);
                    }
                }
            }
        }

        if (!request.IsDirty
            && PublicSurfaceSnapshotStore.IsResolvableRevision(request.HeadRevision)
            && currentEntries is not null)
        {
            PublicSurfaceSnapshotStore.TrySave(workspaceRoot, request.HeadRevision, currentEntries);
        }

        return new SurfaceBaseline(baseline, PublicSurfaceSnapshotStore.ToFingerprints(baseline));
    }

    internal static IReadOnlyDictionary<string, IReadOnlyList<PublicSurfaceEntry>> CaptureEntries(ICodeMapGraphReader reader)
        => reader.PublicSymbols()
            .Where(symbol => PublicSurfaceSnapshotStore.IsRepositoryProject(symbol.Project))
            .GroupBy(symbol => symbol.Project, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PublicSurfaceEntry>)group
                    .Select(symbol => new PublicSurfaceEntry(
                        symbol.Kind.ToString(),
                        symbol.QualifiedName,
                        symbol.Signature ?? string.Empty,
                        symbol.Visibility ?? string.Empty,
                        symbol.Id))
                    .ToArray(),
                StringComparer.Ordinal);

    private static async Task<IReadOnlyDictionary<string, IReadOnlyList<PublicSurfaceEntry>>?> CaptureEntriesFromDatabaseAsync(
        string database,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(database))
            {
                return null;
            }

            await using var reader = await ImpactGraphSession.OpenGraphReaderAsync(database, cancellationToken).ConfigureAwait(false);
            return CaptureEntries(reader);
        }
        catch (Exception exception) when (exception is IOException or FileNotFoundException or DirectoryNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }
}
