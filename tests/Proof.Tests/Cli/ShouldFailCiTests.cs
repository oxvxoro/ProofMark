using Proof.Cli;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

public sealed class ShouldFailCiTests
{
    private static ChangeCertificate Certificate(
        ProofVerdict verdict,
        ProofEvaluation? evaluation = null,
        IReadOnlyList<AnalysisConstraint>? constraints = null)
        => new(
            3,
            "base",
            "head",
            verdict,
            new ChangeImpact("base", "head", [], [], [], [], [], "complete", false),
            new ProofPlan([], Constraints: constraints),
            new VerificationEvidenceSet([], []),
            evaluation ?? new ProofEvaluation(verdict, [], ReasonCode: null));

    private static ProofPolicy Policy(params string[] codes)
        => new(FailOnUncertainCodes: codes);

    [Fact]
    public void Uncertain_ListedReasonCode_Fails()
    {
        var certificate = Certificate(
            ProofVerdict.Uncertain,
            new ProofEvaluation(ProofVerdict.Uncertain, [], ReasonCode: ProofReasonCodes.SourceFreshnessDrift));
        Assert.True(VerifyCommand.ShouldFailCi(certificate, Policy(ProofReasonCodes.SourceFreshnessDrift)));
    }

    [Fact]
    public void Uncertain_UnlistedReasonCode_StaysAdvisory()
    {
        var certificate = Certificate(
            ProofVerdict.Uncertain,
            new ProofEvaluation(ProofVerdict.Uncertain, [], ReasonCode: ProofReasonCodes.SourceFreshnessDrift));
        Assert.False(VerifyCommand.ShouldFailCi(certificate, Policy(ProofReasonCodes.RequiredEvidenceMissing)));
    }

    [Fact]
    public void Uncertain_ListedBlockingConstraint_Fails()
    {
        var constraint = new AnalysisConstraint("c1", ProofReasonCodes.RequiredEvidenceMissing, "blocking", "msg");
        var certificate = Certificate(ProofVerdict.Uncertain, constraints: [constraint]);
        Assert.True(VerifyCommand.ShouldFailCi(certificate, Policy(ProofReasonCodes.RequiredEvidenceMissing)));
    }

    [Fact]
    public void Uncertain_ListedObligationReason_Fails()
    {
        var obligation = new ProofObligation("O1", "P004", ObligationKind.Test, "claim", "t", true, 4, [ProofReasonCodes.RequiredEvidenceMissing]);
        var certificate = Certificate(
            ProofVerdict.Uncertain,
            new ProofEvaluation(ProofVerdict.Uncertain, [new EvaluatedObligation(obligation, ObligationStatus.Unresolved, [])]));
        Assert.True(VerifyCommand.ShouldFailCi(certificate, Policy(ProofReasonCodes.RequiredEvidenceMissing)));
    }

    [Fact]
    public void NoCodesConfigured_NeverFails()
    {
        var certificate = Certificate(
            ProofVerdict.Uncertain,
            new ProofEvaluation(ProofVerdict.Uncertain, [], ReasonCode: ProofReasonCodes.SourceFreshnessDrift));
        Assert.False(VerifyCommand.ShouldFailCi(certificate, new ProofPolicy()));
    }

    [Fact]
    public void ProvenVerdict_NeverFails()
    {
        var certificate = Certificate(ProofVerdict.Proven);
        Assert.False(VerifyCommand.ShouldFailCi(certificate, Policy(ProofReasonCodes.RequiredEvidenceMissing)));
    }

    [Fact]
    public void Uncertain_FallbackRequiredEvidenceMissing_IsAdvisoryWhenNotListed()
    {
        var obligation = new ProofObligation(
            "O1",
            "P005",
            ObligationKind.TestMapping,
            "mapping",
            "s1",
            true,
            2,
            ["no mapped test relation found"]);
        var certificate = Certificate(
            ProofVerdict.Uncertain,
            new ProofEvaluation(ProofVerdict.Uncertain, [new EvaluatedObligation(obligation, ObligationStatus.Unresolved, [])]));

        Assert.False(VerifyCommand.ShouldFailCi(
            certificate,
            Policy(
                ProofReasonCodes.ChangeDeletionAnalysisUnavailable,
                ProofReasonCodes.ChangeCaptureUntrackedFailed,
                ProofReasonCodes.SourceFreshnessDrift)));
    }

    [Fact]
    public void Uncertain_RequiredP005ProseReason_ListedFallbackCode_Fails()
    {
        var obligation = new ProofObligation(
            "O1",
            "P005",
            ObligationKind.TestMapping,
            "mapping",
            "s1",
            true,
            2,
            ["no mapped test relation found"]);
        var certificate = Certificate(
            ProofVerdict.Uncertain,
            new ProofEvaluation(ProofVerdict.Uncertain, [new EvaluatedObligation(obligation, ObligationStatus.Unresolved, [])]));

        Assert.True(VerifyCommand.ShouldFailCi(certificate, Policy(ProofReasonCodes.RequiredEvidenceMissing)));
    }

    [Fact]
    public void Uncertain_AdvisoryP005_NeverFailsByItself()
    {
        var advisory = new ProofObligation(
            "O1",
            "P005",
            ObligationKind.TestMapping,
            "mapping",
            "s1",
            false,
            2,
            ["no mapped test relation found"]);
        var certificate = Certificate(
            ProofVerdict.Uncertain,
            new ProofEvaluation(ProofVerdict.Uncertain, [new EvaluatedObligation(advisory, ObligationStatus.Unresolved, [])]));

        Assert.False(VerifyCommand.ShouldFailCi(certificate, Policy(ProofReasonCodes.RequiredEvidenceMissing)));
    }

    [Fact]
    public void Uncertain_RequiredNonMappingProseReason_StaysAdvisoryEvenWhenFallbackCodeListed()
    {
        // REQUIRED_EVIDENCE_MISSING 폴백은 test-mapping
        // 의무(P005)로 범위가 한정된다. 계획되었으나 묶이지 않은 필수 P004가
        // 이유로 가진 것이 플래너 산문뿐이면 변경 전과 같은 권고 처리를 유지하며
        // 폴백 코드로 병합을 절대 막지 않는다.
        var obligation = new ProofObligation(
            "O1",
            "P004",
            ObligationKind.Test,
            "test",
            "t1",
            true,
            4,
            ["CodeMap found impacted test symbol"]);
        var certificate = Certificate(
            ProofVerdict.Uncertain,
            new ProofEvaluation(ProofVerdict.Uncertain, [new EvaluatedObligation(obligation, ObligationStatus.Unresolved, [])]));

        Assert.False(VerifyCommand.ShouldFailCi(certificate, Policy(ProofReasonCodes.RequiredEvidenceMissing)));
    }

    [Fact]
    public void Uncertain_RequiredArchitectureProseReason_UsesMissingEvidenceFallback()
    {
        var obligation = new ProofObligation(
            "O1",
            "P011",
            ObligationKind.Architecture,
            "architecture policy",
            "architecture-rules",
            true,
            3,
            ["architecture policy check required"]);
        var certificate = Certificate(
            ProofVerdict.Uncertain,
            new ProofEvaluation(ProofVerdict.Uncertain, [new EvaluatedObligation(obligation, ObligationStatus.Unresolved, [])]));

        Assert.True(VerifyCommand.ShouldFailCi(certificate, Policy(ProofReasonCodes.RequiredEvidenceMissing)));
    }

    [Fact]
    public void Uncertain_RequiredCallerContractProseReason_StaysAdvisoryEvenWhenFallbackCodeListed()
    {
        // P002도 범위가 같다. supporting 전용 바인딩(이유가 산문뿐)은
        // 권고로 남고, 의무에 *나열된* 실제 코드는 여전히 차단한다
        // (Uncertain_ListedObligationReason_Fails 참조).
        var obligation = new ProofObligation(
            "O1",
            "P002",
            ObligationKind.CallerContract,
            "caller",
            "c1",
            true,
            3,
            ["CodeMap detected caller relation"]);
        var certificate = Certificate(
            ProofVerdict.Uncertain,
            new ProofEvaluation(ProofVerdict.Uncertain, [new EvaluatedObligation(obligation, ObligationStatus.Unresolved, [])]));

        Assert.False(VerifyCommand.ShouldFailCi(certificate, Policy(ProofReasonCodes.RequiredEvidenceMissing)));
    }

    [Fact]
    public void Uncertain_AdvisoryP005_DoesNotHideRequiredDrift()
    {
        var advisory = new ProofObligation(
            "O1",
            "P005",
            ObligationKind.TestMapping,
            "mapping",
            "s1",
            false,
            2,
            ["no mapped test relation found"]);
        var certificate = Certificate(
            ProofVerdict.Uncertain,
            new ProofEvaluation(ProofVerdict.Uncertain, [new EvaluatedObligation(advisory, ObligationStatus.Unresolved, [])], ReasonCode: ProofReasonCodes.SourceFreshnessDrift));

        Assert.True(VerifyCommand.ShouldFailCi(certificate, Policy(ProofReasonCodes.SourceFreshnessDrift)));
    }
}
