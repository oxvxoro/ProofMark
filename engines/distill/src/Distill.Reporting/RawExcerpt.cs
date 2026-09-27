namespace Distill.Reporting;

public sealed record RawExcerpt(
    string CheckId,
    string ArtifactPath,
    IReadOnlyList<string> Lines,
    string Reason);
