using Proof.Adapters.CodeMap;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

public sealed class YamlTestMappingEvidenceProducerTests
{
    private static readonly TestMapEntry[] Maps =
    [
        new("OrderService.Cancel", ["Proof.Tests.OrderServiceTests.Cancel_keeps_order"])
    ];

    private static ChangeRequest Request() => new("root", "base", "head", [], SourceDigest: "src");

    private static ChangeImpact Impact() => new(
        "base",
        "head",
        [new LineSpan("Services/OrderService.cs", 10, 20)],
        [new ChangedSymbolRef("s1", "App", "Services/OrderService.cs", "OrderService.Cancel", 10, 20, true, false)],
        [],
        ["App"],
        [],
        "complete",
        false,
        SourceDigest: "src");

    private static ProofPlan PlanWithP005() => new(
        [new ProofObligation("O1", "P005", ObligationKind.TestMapping, "mapping", "s1", true, 2, ["r"], new ProofSubject(SubjectKind.Symbol, "s1", "App", DisplayName: "OrderService.Cancel"))],
        SourceDigest: "src");

    [Fact]
    public async Task Producer_MatchedMapEntry_EmitsExactTestMappingEvidence()
    {
        var producer = new YamlTestMappingEvidenceProducer(Maps);
        var evidence = await producer.AnalyzeAsync(Request(), Impact(), PlanWithP005(), CancellationToken.None);

        var item = Assert.Single(evidence);
        Assert.Equal(EvidenceKind.TestMapping, item.Kind);
        Assert.Equal(EvidenceStatus.Pass, item.Status);
        Assert.Equal("test-mapping", item.Provenance.CheckId);
        Assert.Equal("src", item.Provenance.SourceDigest);
        Assert.Equal(ScopeMode.Exact, item.Scope?.Mode);
        Assert.Contains(item.Scope!.SubjectRefs!, subjectRef => subjectRef.Id == "s1");
    }

    [Fact]
    public async Task Producer_UnmatchedSymbol_EmitsNothing()
    {
        var producer = new YamlTestMappingEvidenceProducer([new TestMapEntry("Other.Do", ["Proof.Tests.OtherTests.Do_works"])]);
        var evidence = await producer.AnalyzeAsync(Request(), Impact(), PlanWithP005(), CancellationToken.None);
        Assert.Empty(evidence);
    }

    [Fact]
    public async Task Producer_TwoP005s_OnlyMappedOneGetsEvidence()
    {
        var plan = new ProofPlan(
            [
                new ProofObligation("O1", "P005", ObligationKind.TestMapping, "mapping", "s1", true, 2, ["r"], new ProofSubject(SubjectKind.Symbol, "s1", "App", DisplayName: "OrderService.Cancel")),
                new ProofObligation("O2", "P005", ObligationKind.TestMapping, "mapping", "s2", true, 2, ["r"], new ProofSubject(SubjectKind.Symbol, "s2", "App", DisplayName: "OrderService.Refund"))
            ],
            SourceDigest: "src");
        var producer = new YamlTestMappingEvidenceProducer(Maps);

        var evidence = await producer.AnalyzeAsync(Request(), Impact(), plan, CancellationToken.None);

        var item = Assert.Single(evidence);
        Assert.Equal("s1", item.Subject);
    }

    [Fact]
    public async Task Producer_MappedP005_BindsDirectly_AndProvesObligation()
    {
        var producer = new YamlTestMappingEvidenceProducer(Maps);
        var evidence = await producer.AnalyzeAsync(Request(), Impact(), PlanWithP005(), CancellationToken.None);
        var bound = new EvidenceBinder().Bind(PlanWithP005(), evidence);
        var evaluation = new DeterministicProofEvaluator().Evaluate(PlanWithP005(), bound);

        Assert.Contains(bound.Links, link => link.ObligationId == "O1" && link.Relation == "direct");
        Assert.Equal(ObligationStatus.Proven, evaluation.Obligations[0].Status);
    }

    [Fact]
    public async Task CodeMapProducer_CallerRelations_StillEmitNothing()
    {
        var producer = new CodeMapTestMappingEvidenceProducer();
        var evidence = await producer.AnalyzeAsync(Request(), Impact(), PlanWithP005(), CancellationToken.None);
        Assert.Empty(evidence);
    }

    [Fact]
    public void Planner_MappedChangedSymbol_AddsP004_AndDropsP005()
    {
        var impact = Impact();
        var policy = new ProofPolicy(TestMaps: Maps);

        var plan = new DeterministicProofPlanner().Plan(impact, policy);

        var p005 = plan.Obligations.Where(item => item.RuleId == "P005").ToArray();
        var p004 = plan.Obligations.Where(item => item.RuleId == "P004" && item.SubjectId == "Proof.Tests.OrderServiceTests.Cancel_keeps_order").ToArray();
        Assert.Empty(p005);
        var mapped = Assert.Single(p004);
        Assert.Equal(SubjectKind.Test, mapped.Subject?.Kind);
        Assert.Equal("Proof.Tests.OrderServiceTests.Cancel_keeps_order", mapped.Subject?.DisplayName);
    }

    [Fact]
    public void Planner_UnmappedSymbol_KeepsP005()
    {
        var impact = new ChangeImpact(
            "base",
            "head",
            [new LineSpan("Services/OrderService.cs", 1, 2)],
            [new ChangedSymbolRef("s2", "App", "Services/OrderService.cs", "OrderService.Refund", 1, 2, true, false)],
            [],
            ["App"],
            [],
            "complete",
            false,
            SourceDigest: "src");
        var plan = new DeterministicProofPlanner().Plan(impact, new ProofPolicy(TestMaps: Maps));
        Assert.Contains(plan.Obligations, item => item.RuleId == "P005");
    }

    [Fact]
    public async Task DogfoodStyle_Map_ClosesWholeLoop_Proven()
    {
        // dogfood 맵 키는 CodeMap 표시 이름의 점으로 나뉜 접미사다
        // ("DeterministicProofPlanner.Plan" vs
        // "Proof.Engine.DeterministicProofPlanner.Plan(ChangeImpact, ProofPolicy)").
        // 이 테스트는 전체 루프를 고정한다. 맵 일치 -> P004 + P005 없음 ->
        // YamlTestMappingEvidenceProducer -> 바인더 -> 평가기 PROVEN.
        const string changedFqn = "Proof.Engine.DeterministicProofPlanner.Plan(ChangeImpact, ProofPolicy)";
        const string testFqn = "Proof.Tests.DeterministicProofPlannerTests.Plan_PublicChangedSymbol_AddsCompatibilityObligation";

        var impact = new ChangeImpact(
            "base",
            "head",
            [new LineSpan("src/Proof.Engine/DeterministicProofPlanner.cs", 9, 11)],
            [new ChangedSymbolRef("plan-sym", "Proof.Engine", "src/Proof.Engine/DeterministicProofPlanner.cs", changedFqn, 9, 11, IsPublic: false, IsTest: false)],
            [],
            ["Proof.Engine", "Proof.Tests"],
            [],
            "complete",
            false,
            SourceDigest: "src");
        var policy = new ProofPolicy(TestMaps:
        [
            new TestMapEntry("DeterministicProofPlanner.Plan", [testFqn])
        ]);

        var plan = new DeterministicProofPlanner().Plan(impact, policy);
        Assert.DoesNotContain(plan.Obligations, obligation => obligation.RuleId == "P005");
        // 접미사 방향을 고정한다. 맵 키는 FQN의 점으로 나뉜 접미사다.
        Assert.Contains(plan.Obligations, obligation =>
            obligation.RuleId == "P004" && obligation.SubjectId == testFqn);

        var producer = new YamlTestMappingEvidenceProducer(policy.TestMaps);
        var producerEvidence = await producer.AnalyzeAsync(
            new ChangeRequest("root", "base", "head", [], SourceDigest: "src"),
            impact,
            plan,
            CancellationToken.None);

        var evidence = producerEvidence.ToList();
        evidence.AddRange(plan.Obligations
            .Where(obligation => obligation.Kind == ObligationKind.Test)
            .Select(obligation => new ProofEvidence(
                $"T{obligation.Id}",
                EvidenceKind.TestCase,
                obligation.SubjectId,
                EvidenceStatus.Pass,
                new EvidenceProvenance("distill", SourceDigest: "src", CheckId: "test::Proof.Tests"),
                new EvidenceScope(ScopeMode.Exact, Subjects: [obligation.SubjectId]))));
        evidence.AddRange(plan.Obligations
            .Where(obligation => obligation.Kind is ObligationKind.Build or ObligationKind.CrossProject)
            .Select(obligation => new ProofEvidence(
                $"B{obligation.Id}",
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
                    ]))));

        var evaluation = new DeterministicProofEvaluator().Evaluate(
            plan,
            new EvidenceBinder().Bind(plan, evidence));

        Assert.Equal(ProofVerdict.Proven, evaluation.Verdict);
        var provenP004 = Assert.Single(evaluation.Obligations, item => item.Obligation.RuleId == "P004");
        Assert.Equal(testFqn, provenP004.Obligation.SubjectId);
        Assert.Equal(ObligationStatus.Proven, provenP004.Status);
        Assert.All(evaluation.Obligations, item => Assert.NotEqual(ObligationStatus.Unresolved, item.Status));
    }
}
