using Proof.Adapters.CodeMap;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

public sealed class CodeMapTestMappingEvidenceProducerTests
{
    private static readonly ChangeRequest Request = new("root", "b", "h", [], SourceDigest: "src");

    private static ChangeImpact Impact(params CallerRelation[] callers)
        => new("b", "h", [], [], [], [], [], "complete", false, Callers: callers, SourceDigest: "src");

    private static ProofPlan PlanWithMapping(bool required = true)
        => new([
            new ProofObligation(
                "O1", "P005", ObligationKind.TestMapping, "mapping", "s1", required, 2, ["r"])
        ]);

    [Fact]
    public async Task Producer_EmitsNothing_ForTestCallerRelation()
    {
        // caller 관계는 P005 공백이 없음을 표시할 뿐, 그 증명은 절대 아니다.
        // 플래너는 대신 테스트 caller에 대해 P005를 억제한다.
        var impact = Impact(new CallerRelation(
            "s1", "tc1", "App.Tests", "tests/App.Tests/OrderServiceTests.cs", "Calls", 1.0, IsTest: true));

        var evidence = await new CodeMapTestMappingEvidenceProducer()
            .AnalyzeAsync(Request, impact, PlanWithMapping(), CancellationToken.None);

        Assert.Empty(evidence);
    }

    [Fact]
    public async Task Producer_EmitsNothing_ForNonTestCallerRelation()
    {
        var impact = Impact(new CallerRelation(
            "s1", "cc1", "App", "App/Consumer.cs", "Exact", 1.0, IsTest: false));

        var evidence = await new CodeMapTestMappingEvidenceProducer()
            .AnalyzeAsync(Request, impact, PlanWithMapping(), CancellationToken.None);

        Assert.Empty(evidence);
    }

    [Fact]
    public async Task TestMappingEvidence_DoesNotCloseP005Obligation()
    {
        var plan = PlanWithMapping();
        var impact = Impact(new CallerRelation(
            "s1", "tc1", "App.Tests", "tests/App.Tests/OrderServiceTests.cs", "Calls", 1.0, IsTest: true));
        var evidence = await new CodeMapTestMappingEvidenceProducer()
            .AnalyzeAsync(Request, impact, plan, CancellationToken.None);
        var bound = new EvidenceBinder().Bind(plan, evidence);
        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, bound);
        Assert.Equal(ObligationStatus.Unresolved, evaluation.Obligations[0].Status);
    }

    [Fact]
    public void TestCallerRelation_SuppressesP005AndAddsP004()
    {
        var impact = new ChangeImpact(
            "main",
            "WORKTREE",
            [new LineSpan("App/OrderService.cs", 1, 10)],
            [new ChangedSymbolRef("s1", "App", "App/OrderService.cs", "OrderService.Cancel", 1, 10, IsPublic: false, IsTest: false)],
            [], [], [], "complete", false,
            Callers: [new CallerRelation("s1", "tc1", "App.Tests", "tests/App.Tests/OrderServiceTests.cs", "Calls", 1.0, IsTest: true)]);

        var plan = new DeterministicProofPlanner().Plan(impact);

        Assert.DoesNotContain(plan.Obligations, obligation => obligation.RuleId == "P005");
        Assert.Contains(plan.Obligations, obligation =>
            obligation.RuleId == "P004" && obligation.SubjectId == "tc1");
    }

    [Fact]
    public void AdvisoryTestMappingPolicy_MakesP005NonRequired()
    {
        var policy = new ProofPolicy(TestMappingRequired: false);
        var impact = new ChangeImpact(
            "main",
            "WORKTREE",
            [new LineSpan("App/OrderService.cs", 1, 10)],
            [new ChangedSymbolRef("s1", "App", "App/OrderService.cs", "OrderService.Cancel", 1, 10, IsPublic: false, IsTest: false)],
            [], [], [], "complete", false);

        var plan = new DeterministicProofPlanner().Plan(impact, policy);

        var mapping = Assert.Single(plan.Obligations, obligation => obligation.RuleId == "P005");
        Assert.False(mapping.Required);
    }
}
