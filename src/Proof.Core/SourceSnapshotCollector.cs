namespace Proof.Core;

public static class SourceSnapshotCollector
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
        => SourceSnapshotFactory.Create(
            workspaceRoot,
            baseRevision,
            headRevision,
            files,
            untrackedCaptureFailed,
            usedFileWideFallback,
            isDirty,
            contentHashCaptureFailed,
            structuredDeltaFailed);

    public static ChangeRequest ToChangeRequest(SourceSnapshot snapshot)
        => SourceSnapshotMapper.ToChangeRequest(snapshot);
}
