using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

public sealed class DeterministicProofEvaluatorTests
{
    [Fact]
    public void Evaluate_AllRequiredProven_ReturnsProven()
    {
        var plan = new ProofPlan([
            Obligation("O1", ObligationKind.Build, required: true),
            Obligation("O2", ObligationKind.Test, required: true)
        ]);

        var evidence = new VerificationEvidenceSet(
            [
                Evidence("E1", EvidenceKind.Build, EvidenceStatus.Pass, "build"),
                Evidence("E2", EvidenceKind.TestRun, EvidenceStatus.Pass, "unit")
            ],
            [
                new ObligationEvidenceLink("O1", "E1", "direct", 3),
                new ObligationEvidenceLink("O2", "E2", "direct", 3)
            ]);

        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, evidence);

        Assert.Equal(ProofVerdict.Proven, evaluation.Verdict);
        Assert.All(evaluation.Obligations, item => Assert.Equal(ObligationStatus.Proven, item.Status));
    }

    [Fact]
    public void Evaluate_RequiredFailed_ReturnsNotReady()
    {
        var plan = new ProofPlan([Obligation("O1", ObligationKind.Build, required: true)]);
        var evidence = new VerificationEvidenceSet(
            [Evidence("E1", EvidenceKind.Build, EvidenceStatus.Fail, "build")],
            [new ObligationEvidenceLink("O1", "E1", "direct", 3)]);

        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, evidence);

        Assert.Equal(ProofVerdict.NotReady, evaluation.Verdict);
        Assert.Equal(ObligationStatus.Failed, evaluation.Obligations[0].Status);
    }

    [Fact]
    public void Evaluate_RequiredUnresolved_ReturnsUncertain()
    {
        var plan = new ProofPlan([Obligation("O1", ObligationKind.TestMapping, required: true)]);
        var evidence = new VerificationEvidenceSet(
            [Evidence("E1", EvidenceKind.TestRun, EvidenceStatus.Pass, "unit")],
            []);

        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, evidence);

        Assert.Equal(ProofVerdict.Uncertain, evaluation.Verdict);
        Assert.Equal(ObligationStatus.Unresolved, evaluation.Obligations[0].Status);
    }

    [Fact]
    public void Evaluate_InfraError_ReturnsInfraError()
    {
        var plan = new ProofPlan([Obligation("O1", ObligationKind.Build, required: true)]);
        var evidence = new VerificationEvidenceSet(
            [Evidence("E1", EvidenceKind.Build, EvidenceStatus.InfraError, "build")],
            [new ObligationEvidenceLink("O1", "E1", "direct", 3)]);

        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, evidence);

        Assert.Equal(ProofVerdict.InfraError, evaluation.Verdict);
    }

    [Fact]
    public void Evaluate_PassDistillButRequiredTestUnresolved_ReturnsUncertain()
    {
        var plan = new ProofPlan([
            Obligation("O1", ObligationKind.Build, required: true),
            Obligation("O2", ObligationKind.Test, required: true, subjectId: "missing-test")
        ]);

        var evidence = new VerificationEvidenceSet(
            [
                Evidence("E1", EvidenceKind.Build, EvidenceStatus.Pass, "build"),
                Evidence("E2", EvidenceKind.TestRun, EvidenceStatus.Pass, "unit")
            ],
            [new ObligationEvidenceLink("O1", "E1", "direct", 3)]);

        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, evidence);

        Assert.Equal(ProofVerdict.Uncertain, evaluation.Verdict);
        Assert.Equal(ObligationStatus.Proven, evaluation.Obligations[0].Status);
        Assert.Equal(ObligationStatus.Unresolved, evaluation.Obligations[1].Status);
    }

    private static ProofObligation Obligation(
        string id,
        ObligationKind kind,
        bool required,
        string subjectId = "subject")
        => new(id, "P000", kind, $"claim-{id}", subjectId, required, 1, ["test"]);

    private static ProofEvidence Evidence(string id, EvidenceKind kind, EvidenceStatus status, string subject)
        => new(
            id,
            kind,
            subject,
            status,
            new EvidenceProvenance("distill", CheckId: subject));
}
