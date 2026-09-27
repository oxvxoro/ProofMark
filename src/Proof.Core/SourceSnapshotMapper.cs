namespace Proof.Core;

public static class SourceSnapshotMapper
{
    public static ChangeRequest ToChangeRequest(SourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var spans = snapshot.Files
            .SelectMany(file => file.NewSpans.Count > 0 ? file.NewSpans : file.OldSpans)
            .ToArray();

        return new ChangeRequest(
            snapshot.RepositoryRoot,
            snapshot.BaseCommitSha,
            snapshot.HeadCommitSha,
            spans,
            snapshot.SourceDigest,
            snapshot.ChangeSetIsEmpty,
            snapshot.UsedFileWideFallback,
            snapshot.UntrackedCaptureFailed,
            snapshot.Files,
            snapshot.ContentHashCaptureFailed,
            snapshot.StructuredDeltaFailed,
            snapshot.IsDirty);
    }
}
