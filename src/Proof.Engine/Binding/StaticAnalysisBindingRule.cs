using Proof.Core;

namespace Proof.Engine.Binding;

internal sealed class StaticAnalysisBindingRule : IBindingRule
{
    public (string Relation, int Strength, string RuleId, string ReasonCode, string Explanation)? TryMatch(
        ProofObligation obligation,
        ProofEvidence evidence,
        BindingMatchContext context)
    {
        if (obligation.Kind != ObligationKind.StaticAnalysis || evidence.Kind != EvidenceKind.StaticAnalysis)
        {
            return null;
        }

        if (BindingIdentity.StaticAnalysisProjectMatches(evidence, obligation))
        {
            return ("direct", 3, "BIND_STATIC_ANALYSIS", "SCOPE_MATCH", "Project-scoped static analysis evidence covers this obligation.");
        }

        if (BindingIdentity.IsRepositoryWide(evidence.Scope?.CommandTarget) && evidence.Scope?.SubjectRefs is not { Count: > 0 })
        {
            return ("supporting", 2, "BIND_STATIC_ANALYSIS", "SCOPE_SUPPORTING", "Repository-wide analysis is supporting evidence for this obligation.");
        }

        return null;
    }
}
