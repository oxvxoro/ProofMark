using Proof.Adapters.CodeMap;
using Proof.Core;
using Proof.Adapters.Distill;
using Proof.Cli;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

public sealed class P010AppContractTests
{
    private static ChangedSymbolRef Changed(string id, string displayName = "App.OrderService.Cancel")
        => new(id, "App", "App/OrderService.cs", displayName, 1, 10, IsPublic: false, IsTest: false);

    private static ImpactedSymbolRef Impacted(string id, bool isTest = false, string displayName = "App.OrderService.Cancel")
        => new(id, "App", "App/OrderService.cs", displayName, "root", 1, isTest, "Semantic", 1.0);

    private static ImpactRelation Relation(string edgeKind, string impactedId, string resolutionKind = "Semantic", double? confidence = 1.0)
        => new("root", impactedId, 1, edgeKind, resolutionKind, confidence);

    private static ChangeImpact Impact(ImpactRelation[] relations, ImpactedSymbolRef[] impacted)
        => new(
            "base", "head",
            [new LineSpan("App/OrderService.cs", 1, 2)],
            [Changed("root")],
            impacted,
            ["App"], [],
            "complete", false,
            Relations: relations,
            SourceDigest: "src");

    private static ProofPlan Plan(ChangeImpact impact, bool appContractRequired = true)
        => new DeterministicProofPlanner().Plan(impact, new ProofPolicy
        {
            AppContractRequired = appContractRequired,
            PublicApiCompatibilityRequired = false,
            InternalConsumerCompatibilityRequired = false,
            TestMappingRequired = false,
        });

    [Fact]
    public void Planner_SemanticRoutesToEdge_EmitsP010()
    {
        var plan = Plan(Impact([Relation("RoutesTo", "App.HomeController.Index")], [Impacted("App.HomeController.Index", displayName: "App.HomeController.Index")]));

        var p010 = Assert.Single(plan.Obligations.Where(item => item.RuleId == "P010"));
        Assert.Equal(ObligationKind.AppContract, p010.Kind);
        Assert.Equal("App.HomeController.Index", p010.SubjectId);
        Assert.True(p010.Required);
        Assert.Contains("App contract for 'App.HomeController.Index' remains valid (RoutesTo)", p010.Claim, StringComparison.Ordinal);
    }

    [Fact]
    public void Planner_HeuristicOrLowConfidenceEdges_EmitNoP010()
    {
        var heuristic = Plan(Impact(
            [Relation("RoutesTo", "App.HomeController.Index", resolutionKind: "Heuristic")],
            [Impacted("App.HomeController.Index")]));
        Assert.Empty(heuristic.Obligations.Where(item => item.RuleId == "P010"));

        var lowConfidence = Plan(Impact(
            [Relation("RoutesTo", "App.HomeController.Index", confidence: 0.2)],
            [Impacted("App.HomeController.Index")]));
        Assert.Empty(lowConfidence.Obligations.Where(item => item.RuleId == "P010"));
    }

    [Fact]
    public void Planner_TestSymbols_AndDuplicates_AreExcluded()
    {
        var plan = Plan(Impact(
            [
                Relation("Renders", "View.Tests.Index"),
                Relation("Renders", "App.View.Index")
            ],
            [
                Impacted("View.Tests.Index", isTest: true),
                Impacted("App.View.Index")
            ]));

        var p010 = Assert.Single(plan.Obligations.Where(item => item.RuleId == "P010"));
        Assert.Equal("App.View.Index", p010.SubjectId);
    }

    [Fact]
    public void Planner_PolicyOff_EmitsNoP010()
    {
        var plan = Plan(
            Impact([Relation("RoutesTo", "App.HomeController.Index")], [Impacted("App.HomeController.Index")]),
            appContractRequired: false);
        Assert.Empty(plan.Obligations.Where(item => item.RuleId == "P010"));
    }

    [Fact]
    public void Binder_RuntimeCoverageOfSubject_ProvesP010()
    {
        var obligation = new ProofObligation(
            "O1", "P010", ObligationKind.AppContract, "app contract", "App.HomeController.Index", true, 2, ["r"],
            new ProofSubject(SubjectKind.Symbol, "App.HomeController.Index", "App", DisplayName: "App.HomeController.Index"));
        var plan = new ProofPlan([obligation], SourceDigest: "src");
        var evidence = new ProofEvidence(
            "RC1",
            EvidenceKind.RuntimeCoverage,
            "App.HomeController.Index",
            EvidenceStatus.Pass,
            new EvidenceProvenance("runtime-coverage", CheckId: "runtime-coverage", SourceDigest: "src"),
            new EvidenceScope(
                ScopeMode.Exact,
                SubjectRefs: [new EvidenceSubjectRef(SubjectKind.Symbol, "App.HomeController.Index", "App", DisplayName: "App.HomeController.Index")]));
        var bound = new EvidenceBinder().Bind(plan, [evidence]);

        var link = Assert.Single(bound.Links);
        Assert.Equal("direct", link.Relation);
        Assert.Equal(3, link.Strength);
        Assert.Equal("BIND_APP_COVERAGE", link.BindingRuleId);

        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, bound);
        Assert.Equal(ObligationStatus.Proven, evaluation.Obligations[0].Status);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public async Task RuntimeCoverage_RouteSubject_ClosesOnlyThroughDirectHandler(int depth, bool expectEvidence)
    {
        const string route = "route://App/GET//orders";
        var root = Path.Combine(Path.GetTempPath(), "proof-p010-route-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(
                Path.Combine(root, "coverage.cobertura.xml"),
                """<coverage><packages><package><classes><class name="App.OrderService"><methods><method name="Cancel"><lines><line number="1" hits="1" /></lines></method></methods></class></classes></package></packages></coverage>""");
            var impact = Impact(
                [new ImpactRelation("root", route, depth, "RoutesTo", "Semantic", 1.0)],
                [Impacted(route, displayName: "GET /orders")]);
            var plan = Plan(impact);
            var evidence = await new RuntimeCoverageEvidenceProducer().AnalyzeAsync(
                new ChangeRequest(root, "base", "head", []), impact, plan, ["coverage.cobertura.xml"], CancellationToken.None);

            Assert.Equal(expectEvidence, evidence.Any(item => item.Subject == route));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Binder_TestCaseWithTestFqn_DoesNotProveP010()
    {
        var obligation = new ProofObligation(
            "O1", "P010", ObligationKind.AppContract, "app contract", "App.HomeController.Index", true, 2, ["r"],
            new ProofSubject(SubjectKind.Symbol, "App.HomeController.Index", "App", DisplayName: "App.HomeController.Index"));
        var plan = new ProofPlan([obligation], SourceDigest: "src");
        var evidence = new ProofEvidence(
            "TC1",
            EvidenceKind.TestCase,
            "tc",
            EvidenceStatus.Pass,
            new EvidenceProvenance("distill", CheckId: "test", SourceDigest: "src"),
            new EvidenceScope(
                ScopeMode.Exact,
                SubjectRefs:
                [
                    new EvidenceSubjectRef(
                        SubjectKind.Test,
                        "App.Tests.HomeControllerTests.Index_returns_view",
                        "App.Tests",
                        FullyQualifiedName: "App.Tests.HomeControllerTests.Index_returns_view")
                ]));
        var bound = new EvidenceBinder().Bind(plan, [evidence]);

        Assert.Empty(bound.Links);
        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, bound);
        Assert.Equal(ObligationStatus.Unresolved, evaluation.Obligations[0].Status);
    }

    [Fact]
    public void Binder_BuildOnly_IsSupporting_AndStaysUnresolved()
    {
        var obligation = new ProofObligation(
            "O1", "P010", ObligationKind.AppContract, "app contract", "App.HomeController.Index", true, 2, ["r"],
            new ProofSubject(SubjectKind.Symbol, "App.HomeController.Index", "App", DisplayName: "App.HomeController.Index"));
        var plan = new ProofPlan([obligation], SourceDigest: "src");
        var build = new ProofEvidence(
            "B1",
            EvidenceKind.Build,
            "build",
            EvidenceStatus.Pass,
            new EvidenceProvenance("distill", CheckId: "build", SourceDigest: "src"),
            new EvidenceScope(ScopeMode.Exact, CommandTarget: "src/App/App.csproj"));
        var bound = new EvidenceBinder().Bind(plan, [build]);

        var link = Assert.Single(bound.Links);
        Assert.Equal("supporting", link.Relation);
        Assert.Equal(2, link.Strength);
        Assert.Equal("BIND_APP_BUILD_SUPPORTING", link.BindingRuleId);
        Assert.Equal(ProofReasonCodes.EvidenceTooWeak, link.ReasonCode);

        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, bound);
        Assert.NotEqual(ObligationStatus.Proven, evaluation.Obligations[0].Status);
    }

    [Fact]
    public void VerificationPlanner_AppContract_SelectsCoverageAndUnfilteredTestHost()
    {
        var obligation = new ProofObligation(
            "O1", "P010", ObligationKind.AppContract, "app contract", "App.HomeController.Index", true, 2, ["r"],
            new ProofSubject(SubjectKind.Symbol, "App.HomeController.Index", "App", DisplayName: "App.HomeController.Index"));
        var plan = new ProofPlan([obligation], SourceDigest: "src");
        var catalog = DistillVerificationRunner.MergeProducerCapabilities(
            [
                new EvidenceCapability("build", "build", "src/Proof.slnx", ScopeMode.RepositoryWide, Cost: 2),
                new EvidenceCapability("test", "test", "tests/App.Tests/App.Tests.csproj", ScopeMode.RepositoryWide, Cost: 3)
            ],
            "quick");

        var verificationPlan = new VerificationPlanner().Plan(plan, catalog, "quick");

        Assert.DoesNotContain(verificationPlan.Uncovered, item => item.ObligationId == obligation.Id);
        Assert.Contains(verificationPlan.Checks, item => item.CheckId == DistillVerificationRunner.RuntimeCoverageCheckId);
        Assert.Contains(verificationPlan.Checks, item => item.CheckId == "test");
        Assert.DoesNotContain(verificationPlan.Checks, item => item.CheckId.StartsWith("test::", StringComparison.Ordinal));
        Assert.DoesNotContain(verificationPlan.Checks, item => !string.IsNullOrWhiteSpace(item.TestFilter));
    }

    [Fact]
    public void ImpactAnalysisSettings_ProfileValidation()
    {
        Assert.Equal("code", new ImpactAnalysisSettings().Profile);
        Assert.Equal("app", new ImpactAnalysisSettings(Profile: "app").Profile);
    }
}
