using Proof.Core;

namespace Proof.Engine.Binding;

internal sealed class TestBindingRule : IBindingRule
{
    public (string Relation, int Strength, string RuleId, string ReasonCode, string Explanation)? TryMatch(
        ProofObligation obligation,
        ProofEvidence evidence,
        BindingMatchContext context)
    {
        if (obligation.Kind != ObligationKind.Test)
        {
            return null;
        }

        if (!context.IsTest)
        {
            return null;
        }

        if (context.IsTestCase && BindingIdentity.TestIdentityMatches(evidence, obligation, exact: true))
        {
            return ("direct", 3, "BIND_TEST_CASE", "SCOPE_MATCH", "Test case identity matches the impacted test obligation.");
        }

        if (BindingIdentity.TestIdentityMatches(evidence, obligation, exact: false))
        {
            return ("supporting", 2, "BIND_TEST_HEURISTIC", ProofReasonCodes.EvidenceTooWeak, "Legacy string identity is supporting evidence only.");
        }

        return null;
    }
}
