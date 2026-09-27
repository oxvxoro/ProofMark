namespace Proof.Core;

public static class SourceSnapshotFactory
{
    public static SourceSnapshot Create(
        string workspaceRoot,
        string baseRevision,
        string headRevision,
        IReadOnlyList<FileDelta> files,
        bool untrackedCaptureFailed,
        bool usedFileWideFallback,
        bool isDirty,
        bool contentHashCaptureFailed = false,
        bool structuredDeltaFailed = false)
    {
        var canonical = SourceSnapshotHasher.CanonicalFiles(files);
        var empty = canonical.Count == 0 && !untrackedCaptureFailed && !contentHashCaptureFailed && !structuredDeltaFailed;
        var snapshot = new SourceSnapshot(
            workspaceRoot,
            baseRevision,
            headRevision,
            isDirty,
            SourceDigest: string.Empty,
            canonical,
            empty,
            untrackedCaptureFailed,
            usedFileWideFallback,
            contentHashCaptureFailed,
            structuredDeltaFailed);
        return snapshot with { SourceDigest = SourceSnapshotHasher.Compute(snapshot) };
    }
}
