using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

/// <summary>
/// B3: TestMapping/RuntimeCoverage 증거는 의무 자신의 주체를 지목할
/// 때만 직접 바인딩된다. 매핑 증거는 종류만으로 계획 안의 모든 P005를
/// 절대 닫으면 안 된다.
/// </summary>
public sealed class EvidenceBinderTestMappingTests
{
    private static ProofPlan PlanWithMapping(string subjectId = "s1", string digest = "src")
        => new(
            [new ProofObligation("O1", "P005", ObligationKind.TestMapping, "mapping", subjectId, true, 2, ["r"])],
            SourceDigest: digest);

    private static ProofEvidence MappingEvidence(
        string id,
        string subject,
        IReadOnlyList<EvidenceSubjectRef>? subjectRefs = null,
        string checkId = "test-mapping",
        string digest = "src")
        => new(
            id,
            EvidenceKind.TestMapping,
            subject,
            EvidenceStatus.Pass,
            new EvidenceProvenance("codemap", SourceDigest: digest, CheckId: checkId),
            subjectRefs is null ? null : new EvidenceScope(ScopeMode.Exact, [], null, subjectRefs));

    [Fact]
    public void Binder_BindsSubjectMatchedTestMapping_AsDirect()
    {
        var plan = PlanWithMapping();
        var evidence = MappingEvidence("TM1", "s1", [new EvidenceSubjectRef(SubjectKind.Test, "s1", "App")]);

        var bound = new EvidenceBinder().Bind(plan, [evidence]);

        var link = Assert.Single(bound.Links);
        Assert.Equal("direct", link.Relation);
        Assert.Equal(3, link.Strength);
        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, bound);
        Assert.Equal(ObligationStatus.Proven, evaluation.Obligations[0].Status);
    }

    [Fact]
    public void Binder_BindsEvidenceSubjectMatch_WithoutSubjectRefs_AsDirect()
    {
        var plan = PlanWithMapping();
        var evidence = MappingEvidence("TM1", "s1");

        var bound = new EvidenceBinder().Bind(plan, [evidence]);

        var link = Assert.Single(bound.Links);
        Assert.Equal("direct", link.Relation);
    }

    [Fact]
    public void Binder_DoesNotBindMismatchedSubject_MappingEvidence()
    {
        var plan = PlanWithMapping();
        var evidence = MappingEvidence("TM1", "s2", [new EvidenceSubjectRef(SubjectKind.Test, "s2", "App")]);

        var bound = new EvidenceBinder().Bind(plan, [evidence]);

        Assert.Empty(bound.Links);
        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, bound);
        Assert.Equal(ObligationStatus.Unresolved, evaluation.Obligations[0].Status);
    }

    [Fact]
    public void Binder_SingleMappingEvidence_CannotCloseTwoDifferentP005s()
    {
        var plan = new ProofPlan(
            [
                new ProofObligation("O1", "P005", ObligationKind.TestMapping, "mapping", "s1", true, 2, ["r"]),
                new ProofObligation("O2", "P005", ObligationKind.TestMapping, "mapping", "s2", true, 2, ["r"])
            ],
            SourceDigest: "src");
        var evidence = MappingEvidence("TM1", "s1", [new EvidenceSubjectRef(SubjectKind.Test, "s1", "App")]);

        var bound = new EvidenceBinder().Bind(plan, [evidence]);

        Assert.Single(bound.Links, link => link.ObligationId == "O1");
        Assert.DoesNotContain(bound.Links, link => link.ObligationId == "O2");
    }

    [Fact]
    public void Binder_RejectsMappingEvidence_WithStaleSourceDigest()
    {
        var plan = PlanWithMapping(digest: "current");
        var evidence = MappingEvidence("TM1", "s1", digest: "stale");

        var bound = new EvidenceBinder().Bind(plan, [evidence]);

        Assert.DoesNotContain(bound.Links, link => link.Relation == "direct");
    }

    [Fact]
    public void Binder_RejectsRuntimeCoverage_WithStaleSourceDigest()
    {
        // RuntimeCoverage는 소스에 묶인다. 오래된 다이제스트는 BIND_SOURCE_DIGEST로
        // 거절되어야 하며, 절대 직접 바인딩되면 안 된다.
        var plan = PlanWithMapping(digest: "current");
        var evidence = new ProofEvidence(
            "RC1",
            EvidenceKind.RuntimeCoverage,
            "s1",
            EvidenceStatus.Pass,
            new EvidenceProvenance("runtime-coverage", SourceDigest: "stale", CheckId: "runtime-coverage"),
            new EvidenceScope(ScopeMode.Exact, [], null, [new EvidenceSubjectRef(SubjectKind.Symbol, "s1")]));

        var bound = new EvidenceBinder().Bind(plan, [evidence]);

        Assert.DoesNotContain(bound.Links, link => link.Relation == "direct");
        Assert.Contains(bound.Links, link => link.BindingRuleId == "BIND_SOURCE_DIGEST");
    }

    [Fact]
    public void Binder_BindsSubjectMatchedRuntimeCoverage_AsDirect()
    {
        var plan = PlanWithMapping();
        var evidence = new ProofEvidence(
            "RC1",
            EvidenceKind.RuntimeCoverage,
            "s1",
            EvidenceStatus.Pass,
            new EvidenceProvenance("runtime-coverage", SourceDigest: "src", CheckId: "runtime-coverage"),
            new EvidenceScope(ScopeMode.Exact, [], null, [new EvidenceSubjectRef(SubjectKind.Symbol, "s1")]));

        var bound = new EvidenceBinder().Bind(plan, [evidence]);

        var link = Assert.Single(bound.Links);
        Assert.Equal("direct", link.Relation);
        Assert.Equal(3, link.Strength);
    }
}
