using Proof.Core;

namespace Proof.Engine.Binding;

internal sealed class ManualReviewBindingRule : IBindingRule
{
    public (string Relation, int Strength, string RuleId, string ReasonCode, string Explanation)? TryMatch(
        ProofObligation obligation,
        ProofEvidence evidence,
        BindingMatchContext context)
    {
        if (obligation.Kind != ObligationKind.ManualReview)
        {
            return null;
        }

        if (evidence.Kind == EvidenceKind.ManualReview
            && evidence.Status == EvidenceStatus.Pass
            && evidence.Scope?.SubjectRefs is { Count: > 0 } references
            && references.Any(reference =>
                string.Equals(reference.Id, obligation.SubjectId, StringComparison.Ordinal)))
        {
            return ("direct", 3, "BIND_MANUAL_REVIEW", "SCOPE_MATCH", "Signed manual review covers this path.");
        }

        return null;
    }
}
