using Proof.Core;

namespace Proof.Engine.Binding;

internal interface IBindingRule
{
    (string Relation, int Strength, string RuleId, string ReasonCode, string Explanation)? TryMatch(
        ProofObligation obligation,
        ProofEvidence evidence,
        BindingMatchContext context);
}
