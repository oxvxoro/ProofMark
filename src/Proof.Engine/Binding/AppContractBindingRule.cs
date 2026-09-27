using Proof.Core;

namespace Proof.Engine.Binding;

internal sealed class AppContractBindingRule : IBindingRule
{
    public (string Relation, int Strength, string RuleId, string ReasonCode, string Explanation)? TryMatch(
        ProofObligation obligation,
        ProofEvidence evidence,
        BindingMatchContext context)
    {
        if (obligation.Kind != ObligationKind.AppContract)
        {
            return null;
        }

        if (evidence.Kind == EvidenceKind.RuntimeCoverage
            && BindingIdentity.TestMappingSubjectMatches(evidence, obligation))
        {
            return ("direct", 3, "BIND_APP_COVERAGE", "SCOPE_MATCH", "Runtime coverage of the app-contract symbol covers this obligation.");
        }

        if (context.IsBuild && BindingIdentity.ProjectMatches(evidence, obligation.Subject?.Project ?? obligation.SubjectId, exact: true))
        {
            return ("supporting", 2, "BIND_APP_BUILD_SUPPORTING", ProofReasonCodes.EvidenceTooWeak, "A project build is supporting evidence only for an app contract.");
        }

        if (evidence.Kind == EvidenceKind.StaticAnalysis
            && BindingIdentity.ProjectMatches(evidence, obligation.Subject?.Project ?? obligation.SubjectId, exact: true))
        {
            return ("supporting", 2, "BIND_APP_BUILD_SUPPORTING", ProofReasonCodes.EvidenceTooWeak, "Static analysis is supporting evidence only for an app contract.");
        }

        return null;
    }
}
