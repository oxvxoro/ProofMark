using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

public sealed class DeterministicProofPlannerTests
{
    [Fact]
    public void Plan_PublicChangedSymbol_AddsCompatibilityObligation()
    {
        var impact = new ChangeImpact(
            "main",
            "WORKTREE",
            [new LineSpan("App/OrderService.cs", 1, 10)],
            [new ChangedSymbolRef("s1", "App", "App/OrderService.cs", "OrderService.Cancel", 1, 10, IsPublic: true, IsTest: false)],
            [],
            ["App"],
            [],
            "complete",
            HasHeuristicEdges: false);

        var plan = new DeterministicProofPlanner().Plan(impact);

        Assert.Contains(plan.Obligations, obligation =>
            obligation.RuleId == "P001A" && obligation.Kind == ObligationKind.Compatibility);
    }

    [Fact]
    public void Plan_NoKnownTestCoverage_AddsCoverageGapObligation()
    {
        var impact = new ChangeImpact(
            "main",
            "WORKTREE",
            [new LineSpan("App/OrderService.cs", 1, 10)],
            [new ChangedSymbolRef("s1", "App", "App/OrderService.cs", "OrderService.Cancel", 1, 10, IsPublic: false, IsTest: false)],
            [],
            ["App"],
            [],
            "complete",
            HasHeuristicEdges: false);

        var plan = new DeterministicProofPlanner().Plan(impact);

        Assert.Contains(plan.Obligations, obligation =>
            obligation.RuleId == "P005" && obligation.Kind == ObligationKind.TestMapping);
    }

    [Fact]
    public void Plan_InScopeProject_EmitsRequiredP005()
    {
        var plan = new DeterministicProofPlanner().Plan(Impact("Proof.Engine"), new ProofPolicy(
            TestMappingProjects: ["Proof.Engine"]));

        var p005 = Assert.Single(plan.Obligations, obligation => obligation.RuleId == "P005");
        Assert.True(p005.Required);
    }

    [Fact]
    public void Plan_OutOfScopeProject_EmitsAdvisoryP005()
    {
        var plan = new DeterministicProofPlanner().Plan(Impact("CodeMap.Engine"), new ProofPolicy(
            TestMappingProjects: ["Proof.Engine"]));

        var p005 = Assert.Single(plan.Obligations, obligation => obligation.RuleId == "P005");
        Assert.False(p005.Required);
    }

    [Fact]
    public void Plan_EmptyTestMappingProjects_KeepsGlobalRequiredP005()
    {
        var plan = new DeterministicProofPlanner().Plan(Impact("CodeMap.Engine"), new ProofPolicy(
            TestMappingProjects: []));

        var p005 = Assert.Single(plan.Obligations, obligation => obligation.RuleId == "P005");
        Assert.True(p005.Required);
    }

    [Fact]
    public void Plan_AdvisoryTestMapping_NeverEmitsRequiredP005_EvenInScope()
    {
        var plan = new DeterministicProofPlanner().Plan(Impact("Proof.Engine"), new ProofPolicy(
            TestMappingRequired: false,
            TestMappingProjects: ["Proof.Engine"]));

        var p005 = Assert.Single(plan.Obligations, obligation => obligation.RuleId == "P005");
        Assert.False(p005.Required);
    }

    private static ChangeImpact Impact(string project)
        => new(
            "main",
            "WORKTREE",
            [new LineSpan($"{project}/OrderService.cs", 1, 10)],
            [new ChangedSymbolRef("s1", project, $"{project}/OrderService.cs", "OrderService.Cancel", 1, 10, IsPublic: false, IsTest: false)],
            [],
            [project],
            [],
            "complete",
            HasHeuristicEdges: false);

    [Fact]
    public void Plan_TestCallerRelation_SuppressesP005AndAddsP004()
    {
        var impact = new ChangeImpact(
            "main",
            "WORKTREE",
            [new LineSpan("App/OrderService.cs", 1, 10)],
            [new ChangedSymbolRef("s1", "App", "App/OrderService.cs", "OrderService.Cancel", 1, 10, IsPublic: false, IsTest: false)],
            [],
            ["App"],
            [],
            "complete",
            HasHeuristicEdges: false,
            Callers: [new CallerRelation("s1", "tc1", "App.Tests", "tests/App.Tests/OrderServiceTests.cs", "Exact", 1.0, IsTest: true)]);

        var plan = new DeterministicProofPlanner().Plan(impact);

        Assert.DoesNotContain(plan.Obligations, obligation => obligation.RuleId == "P005");
        var callerTest = Assert.Single(plan.Obligations.Where(obligation => obligation.RuleId == "P004" && obligation.SubjectId == "tc1"));
        Assert.Equal(ObligationKind.Test, callerTest.Kind);
        Assert.True(callerTest.Required);
    }

    [Fact]
    public void Plan_NonTestCallerRelation_StillAddsP005()
    {
        var impact = new ChangeImpact(
            "main",
            "WORKTREE",
            [new LineSpan("App/OrderService.cs", 1, 10)],
            [new ChangedSymbolRef("s1", "App", "App/OrderService.cs", "OrderService.Cancel", 1, 10, IsPublic: false, IsTest: false)],
            [],
            ["App"],
            [],
            "complete",
            HasHeuristicEdges: false,
            Callers: [new CallerRelation("s1", "cc1", "App", "App/OrderConsumer.cs", "Exact", 1.0, IsTest: false)]);

        var plan = new DeterministicProofPlanner().Plan(impact);

        Assert.Contains(plan.Obligations, obligation =>
            obligation.RuleId == "P005" && obligation.Kind == ObligationKind.TestMapping);
        Assert.DoesNotContain(plan.Obligations, obligation => obligation.RuleId == "P004" && obligation.SubjectId == "cc1");
    }

    [Fact]
    public void Plan_DeletedFileDelta_RequiresSignedManualReview()
    {
        var impact = new ChangeImpact(
            "main",
            "WORKTREE",
            [],
            [],
            [],
            [],
            [],
            "complete",
            false,
            FileDeltas: [new FileDelta(FileChangeKind.Deleted, "App/Legacy.cs", null, [], [])]);

        var plan = new DeterministicProofPlanner().Plan(impact);
        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, new VerificationEvidenceSet([], []));

        var obligation = Assert.Single(plan.Obligations, item => item.RuleId == "P009");
        Assert.Equal(ObligationKind.ManualReview, obligation.Kind);
        Assert.Equal("App/Legacy.cs", obligation.SubjectId);
        Assert.Contains(ProofReasonCodes.ChangeDeletionAnalysisUnavailable, obligation.Reasons);
        Assert.DoesNotContain(plan.Constraints ?? [], item =>
            item.Code == ProofReasonCodes.ChangeDeletionAnalysisUnavailable);
        Assert.Equal(ProofVerdict.Uncertain, evaluation.Verdict);
    }

    [Fact]
    public void Plan_DeletedFileDelta_SkipsManualReview_WhenDeletionPathResolved()
    {
        var impact = new ChangeImpact(
            "main",
            "WORKTREE",
            [],
            [],
            [],
            [],
            [],
            "complete",
            false,
            FileDeltas: [new FileDelta(FileChangeKind.Deleted, "App/Legacy.cs", null, [], [])],
            DeletionPathsResolved: ["App/Legacy.cs"]);

        var plan = new DeterministicProofPlanner().Plan(impact);

        Assert.DoesNotContain(plan.Constraints ?? [], item =>
            item.Code == ProofReasonCodes.ChangeDeletionAnalysisUnavailable);
        Assert.DoesNotContain(plan.Obligations, item => item.RuleId == "P009");
    }

    [Fact]
    public void Plan_DeletedFileDelta_RequiresManualReview_WhenOnlyFileNameResolved()
    {
        var impact = new ChangeImpact(
            "main",
            "WORKTREE",
            [],
            [],
            [],
            [],
            [],
            "complete",
            false,
            FileDeltas: [new FileDelta(FileChangeKind.Deleted, "App/Legacy.cs", null, [], [])],
            DeletionPathsResolved: ["Legacy.cs"]);

        var plan = new DeterministicProofPlanner().Plan(impact);

        var obligation = Assert.Single(plan.Obligations, item => item.RuleId == "P009");
        Assert.Equal("App/Legacy.cs", obligation.SubjectId);
        Assert.DoesNotContain(plan.Constraints ?? [], item =>
            item.Code == ProofReasonCodes.ChangeDeletionAnalysisUnavailable);
    }

    [Fact]
    public void PathsAlign_AllowsRepoRelativeDirectorySuffix_NotBareFileName()
    {
        Assert.True(DeterministicProofPlanner.PathsAlign("src/App/Legacy.cs", "App/Legacy.cs"));
        Assert.True(DeterministicProofPlanner.PathsAlign("App/Legacy.cs", "App/Legacy.cs"));
        Assert.False(DeterministicProofPlanner.PathsAlign("App/Legacy.cs", "Legacy.cs"));
        Assert.False(DeterministicProofPlanner.PathsAlign("Other/Legacy.cs", "App/Legacy.cs"));
    }

    [Fact]
    public void Plan_RenamedDelta_SkipsConstraint_WhenOldPathResolved()
    {
        var impact = new ChangeImpact(
            "main",
            "WORKTREE",
            [],
            [],
            [],
            [],
            [],
            "complete",
            false,
            FileDeltas: [new FileDelta(FileChangeKind.Renamed, "App/Legacy.cs", "App/Renamed.cs", [], [])],
            DeletionPathsResolved: ["App/Legacy.cs"]);

        var plan = new DeterministicProofPlanner().Plan(impact);

        Assert.DoesNotContain(plan.Constraints ?? [], item =>
            item.Code == ProofReasonCodes.ChangeDeletionAnalysisUnavailable);
    }

    [Fact]
    public void Plan_DeletionCallerRelations_ProduceCallerContractObligations()
    {
        var impact = new ChangeImpact(
            "main",
            "WORKTREE",
            [],
            [new ChangedSymbolRef("gone-1", "App", "App/Legacy.cs", "Legacy.Do", 1, 10, IsPublic: true, IsTest: false)],
            [],
            ["App", "Consumer"],
            ["caller-1"],
            "complete",
            false,
            FileDeltas: [new FileDelta(FileChangeKind.Deleted, "App/Legacy.cs", null, [], [])],
            DeletionPathsResolved: ["App/Legacy.cs"],
            Callers: [new CallerRelation("gone-1", "caller-1", "Consumer", "Consumer/UseLegacy.cs", "Calls", 1.0, IsTest: true)]);

        var plan = new DeterministicProofPlanner().Plan(impact);

        Assert.DoesNotContain(plan.Constraints ?? [], item =>
            item.Code == ProofReasonCodes.ChangeDeletionAnalysisUnavailable);
        Assert.Contains(plan.Obligations, obligation =>
            obligation.RuleId == "P002" && obligation.SubjectId == "caller-1");
    }

    [Fact]
    public void Plan_ProductionCallerRelations_ProduceNoCallerContractObligation()
    {
        var impact = new ChangeImpact(
            "main",
            "WORKTREE",
            [],
            [new ChangedSymbolRef("gone-1", "App", "App/Legacy.cs", "Legacy.Do", 1, 10, IsPublic: true, IsTest: false)],
            [],
            ["App", "Consumer"],
            ["caller-1"],
            "complete",
            false,
            FileDeltas: [new FileDelta(FileChangeKind.Deleted, "App/Legacy.cs", null, [], [])],
            DeletionPathsResolved: ["App/Legacy.cs"],
            Callers: [new CallerRelation("gone-1", "caller-1", "Consumer", "Consumer/UseLegacy.cs", "Calls", 1.0, IsTest: false)]);

        var plan = new DeterministicProofPlanner().Plan(impact);

        Assert.DoesNotContain(plan.Obligations, obligation => obligation.RuleId == "P002");
        Assert.Contains(plan.Obligations, obligation =>
            obligation.RuleId == "P001B" && obligation.SubjectId == "Consumer");
    }

    [Fact]
    public void Plan_ProductionCrossProjectCaller_NonPublic_AddsP003InsteadOfP002()
    {
        var impact = new ChangeImpact(
            "main",
            "WORKTREE",
            [],
            [new ChangedSymbolRef("s1", "App", "App/Service.cs", "Service.Compute", 1, 10, IsPublic: false, IsTest: false)],
            [],
            ["App", "Consumer"],
            [],
            "complete",
            false,
            Callers: [new CallerRelation("s1", "caller-1", "Consumer", "Consumer/UseService.cs", "Calls", 1.0, IsTest: false)]);

        var plan = new DeterministicProofPlanner().Plan(impact);

        Assert.DoesNotContain(plan.Obligations, obligation => obligation.RuleId == "P002");
        Assert.Contains(plan.Obligations, obligation =>
            obligation.RuleId == "P003" && obligation.SubjectId == "Consumer");
    }

    [Fact]
    public void Plan_ProductionCallerOnly_CanEvaluateProven_WithProjectBuildEvidence()
    {
        var impact = new ChangeImpact(
            "main",
            "WORKTREE",
            [],
            [new ChangedSymbolRef("gone-1", "App", "App/Legacy.cs", "Legacy.Do", 1, 10, IsPublic: false, IsTest: false)],
            [],
            ["App", "Consumer"],
            [],
            "complete",
            false,
            SourceDigest: "src",
            FileDeltas: [new FileDelta(FileChangeKind.Deleted, "App/Legacy.cs", null, [], [])],
            DeletionPathsResolved: ["App/Legacy.cs"],
            Callers: [new CallerRelation("gone-1", "caller-1", "Consumer", "Consumer/UseLegacy.cs", "Calls", 1.0, IsTest: false)]);

        var plan = new DeterministicProofPlanner().Plan(impact, new ProofPolicy(
            TestMaps: [new TestMapEntry("Legacy.Do", ["Proof.Tests.LegacyTests.Do_still_calls"])]));

        Assert.DoesNotContain(plan.Obligations, obligation => obligation.RuleId == "P002");

        // 수용 픽스처: 프로덕션 caller 변경은, 컴파일로 caller 영향이
        // 좁혀지고(모든 build/cross-project 의무에 대한 정확한 프로젝트
        // 빌드) 매핑된 검증 테스트가 통과하면 PROVEN으로 평가된다.
        // caller-compile 규칙은 승격되지 않는다.
        var evidence = plan.Obligations
            .Select((obligation, index) => obligation.Kind is ObligationKind.Build or ObligationKind.CrossProject
                ? new ProofEvidence(
                    $"E{index + 1}",
                    EvidenceKind.Build,
                    obligation.SubjectId,
                    EvidenceStatus.Pass,
                    new EvidenceProvenance("distill", SourceDigest: "src", CheckId: $"build::{obligation.SubjectId}"),
                    new EvidenceScope(
                        ScopeMode.Exact,
                        CommandTarget: $"{obligation.SubjectId}/{obligation.SubjectId}.csproj",
                        SubjectRefs:
                        [
                            new EvidenceSubjectRef(SubjectKind.Project, obligation.SubjectId, obligation.SubjectId)
                        ]))
                : obligation.Kind == ObligationKind.Test
                    ? new ProofEvidence(
                        $"E{index + 1}",
                        EvidenceKind.TestCase,
                        obligation.SubjectId,
                        EvidenceStatus.Pass,
                        new EvidenceProvenance("distill", SourceDigest: "src", CheckId: "test::App.Tests"),
                        new EvidenceScope(
                            ScopeMode.Exact,
                            Subjects: [obligation.SubjectId]))
                    : null)
            .Where(item => item is not null)
            .Cast<ProofEvidence>()
            .ToList();

        var evaluation = new DeterministicProofEvaluator().Evaluate(
            plan,
            new EvidenceBinder().Bind(plan, evidence));

        Assert.Equal(ProofVerdict.Proven, evaluation.Verdict);
        Assert.All(evaluation.Obligations, item =>
            Assert.NotEqual(ObligationStatus.Unresolved, item.Status));
    }
}
