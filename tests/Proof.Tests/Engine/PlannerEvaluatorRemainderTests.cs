using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

public sealed class PlannerEvaluatorRemainderTests
{
    [Fact]
    public void BlockingUncertaintyObligations_HaveDistinctIds()
    {
        var plan = new DeterministicProofPlanner().Plan(new ChangeImpact(
            "b",
            "h",
            [new LineSpan("a.cs", 1, 2)],
            [],
            [],
            [],
            [],
            "partial",
            false,
            Completeness: new ImpactCompleteness(
                CoverageState.Partial,
                CoverageState.PotentiallyTruncated,
                2,
                10,
                10,
                true,
                true,
                0,
                0,
                0.75)));
        var uncertainty = plan.Obligations.Where(item => item.Kind == ObligationKind.Uncertainty).ToArray();
        Assert.True(uncertainty.Length >= 2);
        Assert.Equal(uncertainty.Length, uncertainty.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void DirectFail_TakesPrecedenceOverInfraError()
    {
        var obligation = new ProofObligation("O1", "P004", ObligationKind.Test, "c", "t", true, 4, ["r"], new ProofSubject(SubjectKind.Test, "t", DisplayName: "t"));
        var plan = new ProofPlan([obligation]);
        var fail = new ProofEvidence(
            "E-fail",
            EvidenceKind.TestCase,
            "t",
            EvidenceStatus.Fail,
            new EvidenceProvenance("distill", CheckId: "unit"),
            new EvidenceScope(ScopeMode.Exact, ["t"], SubjectRefs: [new EvidenceSubjectRef(SubjectKind.Test, "t", FullyQualifiedName: "t")]));
        var infra = new ProofEvidence(
            "E-infra",
            EvidenceKind.TestCase,
            "t",
            EvidenceStatus.InfraError,
            new EvidenceProvenance("distill", CheckId: "unit"),
            new EvidenceScope(ScopeMode.Exact, ["t"], SubjectRefs: [new EvidenceSubjectRef(SubjectKind.Test, "t", FullyQualifiedName: "t")]));
        var bound = new EvidenceBinder().Bind(plan, [infra, fail]);
        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, bound);
        Assert.Equal(ObligationStatus.Failed, evaluation.Obligations[0].Status);
        Assert.Equal(ProofVerdict.NotReady, evaluation.Verdict);
    }

    [Fact]
    public void Plan_IsInvariantUnderSymbolPermutation()
    {
        var first = new ChangedSymbolRef("s1", "App", "a.cs", "A", 1, 2, true, false);
        var second = new ChangedSymbolRef("s2", "App", "b.cs", "B", 3, 4, true, false);
        var left = new DeterministicProofPlanner().Plan(Impact([first, second]));
        var right = new DeterministicProofPlanner().Plan(Impact([second, first]));
        Assert.Equal(
            left.Obligations.Select(item => item.Id).ToArray(),
            right.Obligations.Select(item => item.Id).ToArray());
        Assert.Equal(left.Obligations.Select(item => item.SubjectId).ToArray(), right.Obligations.Select(item => item.SubjectId).ToArray());
    }

    private static ChangeImpact Impact(IReadOnlyList<ChangedSymbolRef> changed)
        => new("b", "h", [], changed, [], ["App"], [], "complete", false);
}
