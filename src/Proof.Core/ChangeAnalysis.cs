namespace Proof.Core;

public enum ApiCompatibilityState
{
    Unknown,
    Unchanged,
    Additive,
    Breaking,
    Mixed
}

public sealed record ApiCompatibilityFact(
    string Project,
    ApiCompatibilityState State);

public sealed record ChangeAnalysisResult(
    ChangeImpact Impact,
    IReadOnlyList<ApiCompatibilityFact> ApiCompatibilityFacts);
