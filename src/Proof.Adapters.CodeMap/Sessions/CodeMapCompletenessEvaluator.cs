using Proof.Core;

namespace Proof.Adapters.CodeMap.Sessions;

internal readonly record struct CodeMapCompletenessEvaluation(
    ImpactCompleteness Completeness,
    string CoverageStatus,
    bool HasHeuristic,
    IReadOnlyList<AnalysisConstraint> Constraints);

internal static class CodeMapCompletenessEvaluator
{
    internal static CodeMapCompletenessEvaluation Evaluate(
        int unknownSpanCount,
        int impactCount,
        bool impactTruncated,
        bool callerTruncated,
        int heuristicCount,
        double? lowestConfidence,
        ResolvedImpactBudget budget)
    {
        var hasHeuristic = heuristicCount > 0;
        var locationState = unknownSpanCount > 0 ? CoverageState.Partial : CoverageState.Complete;
        var traversalState = impactTruncated || callerTruncated
            ? CoverageState.PotentiallyTruncated
            : CoverageState.Complete;
        var coverageStatus = locationState != CoverageState.Complete || traversalState != CoverageState.Complete
            ? "partial"
            : "complete";

        var completeness = new ImpactCompleteness(
            locationState,
            traversalState,
            budget.Depth,
            budget.MaxResults,
            budget.CallerMaxResults,
            impactTruncated,
            callerTruncated,
            unknownSpanCount,
            heuristicCount,
            impactCount == 0 ? null : lowestConfidence);

        var constraints = new List<AnalysisConstraint>();
        if (impactTruncated)
        {
            constraints.Add(new AnalysisConstraint("C-impact-limit", ProofReasonCodes.ImpactPotentiallyTruncated, "blocking", "Impact result limit reached."));
        }

        if (callerTruncated)
        {
            constraints.Add(new AnalysisConstraint("C-caller-limit", ProofReasonCodes.CallerPotentiallyTruncated, "blocking", "Caller result limit reached."));
        }

        return new CodeMapCompletenessEvaluation(completeness, coverageStatus, hasHeuristic, constraints);
    }
}
