using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

public sealed class StaticAnalysisBinderTests
{
    private static ProofPlan PlanWithAnalysis(string projectId = "App", string digest = "src")
        => new(
            [new ProofObligation("O8", "P008", ObligationKind.StaticAnalysis, "analysis", projectId, true, 2, ["r"])],
            SourceDigest: digest);

    private static ProofEvidence AnalysisEvidence(
        string subject,
        IReadOnlyList<EvidenceSubjectRef>? subjectRefs,
        string commandTarget,
        EvidenceStatus status = EvidenceStatus.Pass)
        => new(
            "SA1",
            EvidenceKind.StaticAnalysis,
            subject,
            status,
            new EvidenceProvenance("distill", SourceDigest: "src", CheckId: "lint"),
            new EvidenceScope(ScopeMode.Exact, [], commandTarget, subjectRefs));

    [Fact]
    public void Binder_BindsProjectScopedAnalysis_AsDirect()
    {
        var plan = PlanWithAnalysis();
        var evidence = AnalysisEvidence("lint", [new EvidenceSubjectRef(SubjectKind.Project, "App", "App")], "src/App/App.csproj");

        var bound = new EvidenceBinder().Bind(plan, [evidence]);

        var link = Assert.Single(bound.Links);
        Assert.Equal("direct", link.Relation);
        Assert.Equal(3, link.Strength);
        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, bound);
        Assert.Equal(ObligationStatus.Proven, evaluation.Obligations[0].Status);
    }

    [Fact]
    public void Binder_BindsRepositoryWideAnalysis_AsSupportingOnly()
    {
        var plan = PlanWithAnalysis();
        var evidence = AnalysisEvidence("lint", null, "App.slnx");

        var bound = new EvidenceBinder().Bind(plan, [evidence]);

        var link = Assert.Single(bound.Links);
        Assert.Equal("supporting", link.Relation);
        Assert.Equal(2, link.Strength);
        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, bound);
        Assert.NotEqual(ObligationStatus.Proven, evaluation.Obligations[0].Status);
    }

    [Fact]
    public void Binder_DoesNotBindAnalysisForDifferentProject()
    {
        var plan = PlanWithAnalysis("App");
        var evidence = AnalysisEvidence("lint", [new EvidenceSubjectRef(SubjectKind.Project, "Other", "Other")], "src/Other/Other.csproj");

        var bound = new EvidenceBinder().Bind(plan, [evidence]);

        Assert.Empty(bound.Links);
    }

    [Fact]
    public void Binder_FailedAnalysis_EvidenceCannotProveObligation()
    {
        var plan = PlanWithAnalysis();
        var evidence = AnalysisEvidence(
            "lint",
            [new EvidenceSubjectRef(SubjectKind.Project, "App", "App")],
            "src/App/App.csproj",
            EvidenceStatus.Fail);

        var bound = new EvidenceBinder().Bind(plan, [evidence]);

        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, bound);
        Assert.Equal(ObligationStatus.Failed, evaluation.Obligations[0].Status);
    }
}

public sealed class StaticAnalysisPlannerTests
{
    private static ChangeImpact ImpactWithProjects(params string[] projects)
        => new(
            "b",
            "h",
            [],
            projects.Select(project => new ChangedSymbolRef($"s-{project}", project, $"{project}/A.cs", "A.Cls", 1, 5, IsPublic: true, IsTest: false)).ToArray(),
            [],
            projects,
            [],
            "complete",
            false,
            SourceDigest: "src");

    [Fact]
    public void Planner_NoStaticAnalysisObligation_ByDefaultPolicy()
    {
        var plan = new DeterministicProofPlanner().Plan(ImpactWithProjects("App"));

        Assert.DoesNotContain(plan.Obligations, obligation => obligation.RuleId == "P008");
    }

    [Fact]
    public void Planner_AddsP008PerImpactedProject_WhenPolicyRequired()
    {
        var policy = new ProofPolicy(StaticAnalysisRequired: true);
        var plan = new DeterministicProofPlanner().Plan(ImpactWithProjects("App", "Consumer"), policy);

        var p008 = plan.Obligations.Where(obligation => obligation.RuleId == "P008").ToArray();
        Assert.Equal(2, p008.Length);
        Assert.All(p008, obligation => Assert.True(obligation.Required));
        Assert.All(p008, obligation => Assert.Equal(ObligationKind.StaticAnalysis, obligation.Kind));
    }

    [Fact]
    public void Planner_NoP008_WhenPolicyOff()
    {
        var policy = new ProofPolicy(StaticAnalysisRequired: false);
        var plan = new DeterministicProofPlanner().Plan(ImpactWithProjects("App"), policy);

        Assert.DoesNotContain(plan.Obligations, obligation => obligation.RuleId == "P008");
    }

    [Fact]
    public void VerificationPlanner_AnalysisCapability_BecomesDonorForP008Clones()
    {
        // Wave B 5단계: 정확한 범위의 분석 능력은 P008의
        // override 제공자여야 한다. 프로젝트 클론(check::Project)이
        // OverrideTarget과 함께 계획되도록.
        var planner = new VerificationPlanner();
        var catalog = new[]
        {
            new EvidenceCapability("analyzers", "analysis", "Proof.slnx", ScopeMode.RepositoryWide)
        };
        var obligation = new ProofObligation(
            "O8",
            "P008",
            ObligationKind.StaticAnalysis,
            "analysis",
            "App",
            true,
            2,
            ["r"],
            new ProofSubject(SubjectKind.Project, "App", "App"));
        var plan = new ProofPlan([obligation]);

        var verificationPlan = planner.Plan(plan, catalog, "full");

        var check = Assert.Single(verificationPlan.Checks);
        Assert.Equal("analyzers::App", check.CheckId);
        Assert.Equal("App", check.OverrideTarget);
        Assert.Contains(obligation.Id, check.SatisfiesObligationIds ?? []);
        Assert.DoesNotContain(verificationPlan.Uncovered, uncovered => uncovered.ObligationId == obligation.Id);
    }

    [Fact]
    public void VerificationPlanner_AnalysisPassWithProjectSubjectRefs_ProvesP008()
    {
        // 프로젝트 클론에 대한 시뮬레이션된 깨끗한 분석: 빈 진단,
        // 정확한 범위, 프로젝트 SubjectRef → P008 Proven(바인더/평가기
        // 수준. roslyn 분석기는 필요 없다).
        var plan = new ProofPlan(
            [new ProofObligation("O8", "P008", ObligationKind.StaticAnalysis, "analysis", "App", true, 2, ["r"], new ProofSubject(SubjectKind.Project, "App", "App"))],
            SourceDigest: "src");
        var evidence = new ProofEvidence(
            "SA1",
            EvidenceKind.StaticAnalysis,
            "analyzers::App",
            EvidenceStatus.Pass,
            new EvidenceProvenance("distill", CheckId: "analyzers::App", SourceDigest: "src"),
            new EvidenceScope(
                ScopeMode.Exact,
                [],
                "src/App/App.csproj",
                [new EvidenceSubjectRef(SubjectKind.Project, "App", "App")]));

        var bound = new EvidenceBinder().Bind(plan, [evidence]);
        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, bound);

        Assert.Equal(ObligationStatus.Proven, evaluation.Obligations[0].Status);
    }
}
