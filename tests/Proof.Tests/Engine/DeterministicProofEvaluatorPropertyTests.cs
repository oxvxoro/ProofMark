using FsCheck;
using FsCheck.Xunit;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

public sealed class DeterministicProofEvaluatorPropertyTests
{
    [Property]
    public bool NonEmptyChangeWithEmptyPlan_IsAlwaysUncertain(PositiveInt seed)
    {
        var plan = new ProofPlan([], ChangeSetIsEmpty: false, SourceDigest: $"digest-{seed.Get}");
        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, new VerificationEvidenceSet([], []));
        return evaluation.Verdict == ProofVerdict.Uncertain
               && evaluation.ReasonCode == ProofReasonCodes.ChangeNonemptyPlanEmpty;
    }

    [Property]
    public bool EmptyChange_IsAlwaysNoChange(PositiveInt seed)
    {
        var plan = new ProofPlan([], ChangeSetIsEmpty: true, SourceDigest: $"digest-{seed.Get}");
        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, new VerificationEvidenceSet([], []));
        return evaluation.Verdict == ProofVerdict.NoChange;
    }

    [Property]
    public bool UnrelatedPassCannotImproveMissingRequired(NonEmptyString subjectId)
    {
        var plan = new ProofPlan([
            new ProofObligation("O1", "P004", ObligationKind.Test, "claim", subjectId.Get, true, 4, ["r"])
        ]);
        var without = new VerificationEvidenceSet([], []);
        var withUnrelated = new VerificationEvidenceSet(
            [
                new ProofEvidence(
                    "E1",
                    EvidenceKind.TestRun,
                    "other",
                    EvidenceStatus.Pass,
                    new EvidenceProvenance("distill"),
                    new EvidenceScope(Subjects: ["unrelated-test-name"]))
            ],
            []);

        var left = new DeterministicProofEvaluator().Evaluate(plan, without);
        var right = new DeterministicProofEvaluator().Evaluate(plan, withUnrelated);
        return left.Verdict == right.Verdict && left.Obligations[0].Status == right.Obligations[0].Status;
    }
}
