using Proof.Core;

namespace Proof.Engine.Binding;

internal sealed class BuildCrossProjectBindingRule : IBindingRule
{
    public (string Relation, int Strength, string RuleId, string ReasonCode, string Explanation)? TryMatch(
        ProofObligation obligation,
        ProofEvidence evidence,
        BindingMatchContext context)
    {
        if (obligation.Kind is not (ObligationKind.Build or ObligationKind.CrossProject))
        {
            return null;
        }

        if (!context.IsBuild)
        {
            return null;
        }

        if (BindingIdentity.ProjectMatches(evidence, obligation.SubjectId, exact: true))
        {
            return ("direct", 3, "BIND_BUILD_PROJECT", "SCOPE_MATCH", "Exact project build target covers the obligation.");
        }

        if (BindingIdentity.ProjectMatches(evidence, obligation.SubjectId, exact: false))
        {
            return ("supporting", 2, "BIND_BUILD_PROJECT_HEURISTIC", ProofReasonCodes.EvidenceTooWeak, "Partial project name match is supporting evidence only.");
        }

        if (context.RepositoryWide)
        {
            return ("supporting", 2, "BIND_BUILD_SOLUTION", "SCOPE_CONTAINS", "Solution-wide build is supporting evidence for the project.");
        }

        return null;
    }
}
