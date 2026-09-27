using Proof.Core;

namespace Proof.Engine.Binding;

internal sealed class TestMappingBindingRule : IBindingRule
{
    public (string Relation, int Strength, string RuleId, string ReasonCode, string Explanation)? TryMatch(
        ProofObligation obligation,
        ProofEvidence evidence,
        BindingMatchContext context)
    {
        if (obligation.Kind != ObligationKind.TestMapping)
        {
            return null;
        }

        if (evidence.Kind is EvidenceKind.TestMapping or EvidenceKind.RuntimeCoverage
            && BindingIdentity.TestMappingSubjectMatches(evidence, obligation))
        {
            return ("direct", 3, "BIND_TEST_MAPPING", "SCOPE_MATCH", "Subject-matched mapping evidence covers this obligation.");
        }

        return null;
    }
}
