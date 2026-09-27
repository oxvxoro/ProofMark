using Proof.Core;

namespace Proof.Engine.Binding;

internal sealed class ArchitectureBindingRule : IBindingRule
{
    public (string Relation, int Strength, string RuleId, string ReasonCode, string Explanation)? TryMatch(
        ProofObligation obligation,
        ProofEvidence evidence,
        BindingMatchContext context)
    {
        if (obligation.Kind != ObligationKind.Architecture)
        {
            return null;
        }

        if (evidence.Kind == EvidenceKind.Architecture
            && BindingIdentity.TestMappingSubjectMatches(evidence, obligation))
        {
            return ("direct", 3, "BIND_ARCHITECTURE", "SCOPE_MATCH", "Subject-matched architecture evidence covers this obligation.");
        }

        return null;
    }
}
