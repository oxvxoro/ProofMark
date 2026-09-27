using Proof.Core;

namespace Proof.Engine.Binding;

internal sealed class CallerContractBindingRule : IBindingRule
{
    public (string Relation, int Strength, string RuleId, string ReasonCode, string Explanation)? TryMatch(
        ProofObligation obligation,
        ProofEvidence evidence,
        BindingMatchContext context)
    {
        if (obligation.Kind != ObligationKind.CallerContract)
        {
            return null;
        }

        if (context.IsTestCase && BindingIdentity.TestIdentityMatches(evidence, obligation, exact: true))
        {
            return ("direct", 3, "BIND_CALLER_TEST", "SCOPE_MATCH", "Test case identity matches the caller contract.");
        }

        if (context.IsTest && BindingIdentity.TestIdentityMatches(evidence, obligation, exact: false))
        {
            return ("supporting", 2, "BIND_CALLER_TEST_HEURISTIC", ProofReasonCodes.EvidenceTooWeak, "Legacy caller test identity is supporting evidence only.");
        }

        if (context.IsBuild && BindingIdentity.ProjectMatches(evidence, obligation.Subject?.Project ?? obligation.SubjectId, exact: true))
        {
            return ("supporting", 2, "BIND_CALLER_COMPILE", "SCOPE_CONTAINS", "Caller project compile is supporting caller-contract evidence.");
        }

        if (context.IsTest && BindingIdentity.CommandTargetMatchesProject(evidence, obligation.Subject?.Project))
        {
            return ("supporting", 2, "BIND_CALLER_TEST_PROJECT", "SCOPE_CONTAINS", "Test command targets the caller project.");
        }

        return null;
    }
}
