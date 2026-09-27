using Proof.Core;

namespace Proof.Engine.Binding;

internal sealed class CompatibilityBindingRule : IBindingRule
{
    public (string Relation, int Strength, string RuleId, string ReasonCode, string Explanation)? TryMatch(
        ProofObligation obligation,
        ProofEvidence evidence,
        BindingMatchContext context)
    {
        if (obligation.Kind != ObligationKind.Compatibility)
        {
            return null;
        }

        if (context.IsApi)
        {
            var project = obligation.Subject?.Project ?? obligation.SubjectId;
            if (BindingIdentity.ApiCompatibilityProjectMatches(evidence, project)
                || BindingIdentity.ApiCompatibilitySymbolMatches(evidence, obligation))
            {
                return ("direct", 3, "BIND_API_COMPAT", "SCOPE_MATCH", "API compatibility evidence covers the public API claim.");
            }

            if (context.RepositoryWide)
            {
                return ("supporting", 2, "BIND_API_COMPAT_SUPPORTING", ProofReasonCodes.EvidenceTooWeak, "Repository-wide API compatibility is supporting evidence only.");
            }

            return null;
        }

        if (context.IsBuild)
        {
            return ("supporting", 2, "BIND_BUILD_SUPPORTING_API", ProofReasonCodes.EvidenceTooWeak, "Generic build cannot directly prove public API compatibility.");
        }

        return null;
    }
}
