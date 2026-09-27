using Proof.Core;
using Proof.Engine.Planning.Specifications;

namespace Proof.Engine.Planning;

internal sealed class UncertaintyObligationRule : IObligationRule
{
    private static readonly CompleteCallerCoverageSpecification CompleteCallerCoverage = new();

    public string RuleId => "Uncertainty";

    public void Apply(PlanningContext context)
    {
        var impact = context.Impact;
        var policy = context.Policy;
        var constraints = context.Constraints;
        var obligations = context.Obligations;
        var completeness = impact.Completeness;

        var locationUnknown = string.Equals(impact.CoverageStatus, "partial", StringComparison.OrdinalIgnoreCase)
            || completeness?.Location is CoverageState.Partial or CoverageState.Unknown;
        if (locationUnknown)
        {
            ObligationPlanningSupport.AddConstraintOrObligation(
                constraints,
                obligations,
                ProofReasonCodes.ImpactLocationUnknown,
                "Impact coverage is partial or unknown",
                policy.LocationUnknown);
        }

        if (completeness?.ImpactPotentiallyTruncated == true)
        {
            ObligationPlanningSupport.AddConstraintOrObligation(
                constraints,
                obligations,
                ProofReasonCodes.ImpactPotentiallyTruncated,
                "Impact traversal reached a result limit",
                policy.ImpactTruncated);
        }

        if (!CompleteCallerCoverage.Evaluate(completeness).IsSatisfied)
        {
            ObligationPlanningSupport.AddConstraintOrObligation(
                constraints,
                obligations,
                ProofReasonCodes.CallerPotentiallyTruncated,
                "Caller collection reached a result limit",
                policy.CallerTruncated);
        }

        if (impact.HasHeuristicEdges)
        {
            ObligationPlanningSupport.AddConstraintOrObligation(
                constraints,
                obligations,
                ProofReasonCodes.HeuristicEdgeBlocking,
                "Heuristic or low-confidence impact requires review",
                policy.HeuristicImpact);
        }

        if (impact.UsedFileWideFallback)
        {
            constraints.Add(ObligationPlanningSupport.Constraint(
                ProofReasonCodes.FileWideFallback,
                "advisory",
                "Git hunks were unavailable; file-wide spans were used."));
        }
    }
}
