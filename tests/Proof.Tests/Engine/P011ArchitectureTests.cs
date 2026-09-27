using System.Text.Json;
using Proof.Adapters.CodeMap;
using Proof.Adapters.Distill;
using Proof.Cli;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

public sealed class P011ArchitectureTests
{
    private static ChangedSymbolRef Changed(string id, string displayName = "App.OrderService.Cancel")
        => new(id, "App", "App/OrderService.cs", displayName, 1, 10, IsPublic: false, IsTest: false);

    private static ChangeImpact Impact(
        IReadOnlyList<ArchitectureViolationRef>? violations,
        bool? rulesPresent = true)
        => new(
            "base", "head",
            [new LineSpan("App/OrderService.cs", 1, 2)],
            [Changed("App.OrderService.Cancel")],
            [],
            ["App"], [],
            "complete", false,
            SourceDigest: "src",
            ArchitectureViolations: violations,
            ArchitectureRulesPresent: rulesPresent,
            ArchitectureRulesDigest: rulesPresent == true ? "rules-sha256" : null);

    private static ProofPolicy Policy(ArchitecturePolicyMode mode)
        => new()
        {
            PublicApiCompatibilityRequired = false,
            InternalConsumerCompatibilityRequired = false,
            TestMappingRequired = false,
            Architecture = mode
        };

    private static ProofPlan Plan(ChangeImpact impact, ProofPolicy? policy = null)
        => new DeterministicProofPlanner().Plan(impact, policy ?? Policy(ArchitecturePolicyMode.Required));

    private static ArchitectureViolationRef Cycle(string project = "App")
        => new("cycle", $"Circular project reference: {project} -> Base -> {project}", project);

    private static ArchitectureViolationRef Layer(string subjectId = "App.OrderService.Cancel")
        => new("layer", "ui must not reference domain: App.View -> App.OrderService", subjectId, "App", "App/OrderService.cs", "App.OrderService.Cancel");

    private static ArchitectureViolationRef FanOut(string subjectId = "App.OrderService.Cancel")
        => new("fan-out", $"{subjectId} fan-out 55 exceeds 40", subjectId, "App", "App/OrderService.cs", subjectId);

    private static ProofEvidence ArchitectureEvidence(
        string subjectId,
        EvidenceStatus status,
        string? sourceDigest = "src",
        SubjectKind subjectKind = SubjectKind.Symbol)
        => new(
            "A1",
            EvidenceKind.Architecture,
            subjectId,
            status,
            new EvidenceProvenance("architecture", CheckId: "architecture", SourceDigest: sourceDigest),
            new EvidenceScope(
                ScopeMode.Exact,
                SubjectRefs: [new EvidenceSubjectRef(subjectKind, subjectId)]));

    private static ProofObligation P011(string subjectId, SubjectKind kind = SubjectKind.Symbol, bool required = true)
        => new(
            "O1", "P011", ObligationKind.Architecture, "architecture rules", subjectId, required, 3, ["r"],
            new ProofSubject(kind, subjectId, kind == SubjectKind.Symbol ? "App" : null));

    // --- 플래너 ---

    [Fact]
    public void Planner_RequiredCycleViolation_EmitsRequiredP011WithProjectSubject()
    {
        var plan = Plan(Impact([Cycle("App")]));

        var p011 = Assert.Single(plan.Obligations.Where(item => item.RuleId == "P011" && item.SubjectId == "App"));
        Assert.Equal(ObligationKind.Architecture, p011.Kind);
        Assert.Equal("App", p011.SubjectId);
        Assert.Equal(SubjectKind.Project, p011.Subject!.Kind);
        Assert.True(p011.Required);
        Assert.Equal(3, p011.RiskWeight);
        Assert.Contains("cycle", p011.Claim, StringComparison.Ordinal);
    }

    [Fact]
    public void Planner_RequiredLayerViolation_EmitsRequiredP011WithSymbolSubject()
    {
        var plan = Plan(Impact([Layer()]));

        var p011 = Assert.Single(plan.Obligations.Where(item => item.RuleId == "P011" && item.SubjectId == "App.OrderService.Cancel"));
        Assert.True(p011.Required);
        Assert.Equal("App.OrderService.Cancel", p011.SubjectId);
        Assert.Equal(SubjectKind.Symbol, p011.Subject!.Kind);
        Assert.Equal("App", p011.Subject.Project);
    }

    [Fact]
    public void Planner_RequiredFanKinds_AreAdvisoryOnly()
    {
        var plan = Plan(Impact(
        [
            new ArchitectureViolationRef("fan-in", "fan-in 55 exceeds 40", "App.A"),
            new ArchitectureViolationRef("fan-out", "fan-out 55 exceeds 40", "App.B"),
            new ArchitectureViolationRef("orphan", "Public symbol has no inbound relations: App.C", "App.C")
        ]));

        var p011s = plan.Obligations.Where(item => item.RuleId == "P011").ToArray();
        Assert.Equal(4, p011s.Length);
        Assert.All(p011s.Where(item => item.SubjectId != "architecture-rules"), item => Assert.False(item.Required));
        Assert.True(p011s.Single(item => item.SubjectId == "architecture-rules").Required);
    }

    [Fact]
    public void Planner_AdvisoryMode_AllP011AreAdvisory()
    {
        var plan = Plan(Impact([Cycle(), Layer()]), Policy(ArchitecturePolicyMode.Advisory));

        var p011s = plan.Obligations.Where(item => item.RuleId == "P011").ToArray();
        Assert.Equal(2, p011s.Length);
        Assert.All(p011s, item => Assert.False(item.Required));
    }

    [Fact]
    public void Planner_OffMode_EmitsNoP011()
    {
        var plan = Plan(Impact([Cycle(), Layer()]), Policy(ArchitecturePolicyMode.Off));
        Assert.Empty(plan.Obligations.Where(item => item.RuleId == "P011"));
    }

    [Fact]
    public void Planner_RequiredWithMissingRules_EmitsRepositoryObligation()
    {
        var plan = Plan(Impact(violations: null, rulesPresent: false));

        var p011 = Assert.Single(plan.Obligations.Where(item => item.RuleId == "P011"));
        Assert.True(p011.Required);
        Assert.Equal(3, p011.RiskWeight);
        Assert.Equal("architecture-rules", p011.SubjectId);
        Assert.Equal(SubjectKind.Repository, p011.Subject!.Kind);
    }

    [Fact]
    public void Planner_RequiredWithMissingRules_AndViolations_EmitsBothObligations()
    {
        var plan = Plan(Impact([Cycle("App")], rulesPresent: false));

        var p011s = plan.Obligations.Where(item => item.RuleId == "P011").ToArray();
        Assert.Equal(2, p011s.Length);

        var gap = Assert.Single(p011s, item => item.SubjectId == "architecture-rules");
        Assert.True(gap.Required);
        Assert.Equal(SubjectKind.Repository, gap.Subject!.Kind);
        Assert.Contains("rules file missing", gap.Reasons[0], StringComparison.Ordinal);

        var cycle = Assert.Single(p011s, item => item.SubjectId == "App");
        Assert.True(cycle.Required);
        Assert.Equal(SubjectKind.Project, cycle.Subject!.Kind);
        Assert.Contains("cycle", cycle.Reasons, StringComparer.Ordinal);
    }

    [Fact]
    public void Planner_RequiredWithMissingRules_CycleStillMergeBlocks()
    {
        var plan = Plan(Impact([Cycle("App")], rulesPresent: false));
        var bound = new EvidenceBinder().Bind(plan, [ArchitectureEvidence("App", EvidenceStatus.Fail)]);

        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, bound);

        var cycle = evaluation.Obligations.Single(item => item.Obligation.SubjectId == "App");
        Assert.Equal(ObligationStatus.Failed, cycle.Status);
        Assert.Equal(ProofVerdict.NotReady, evaluation.Verdict);
    }

    [Fact]
    public void Planner_AdvisoryWithMissingRules_KeepsAdvisoryPerSubjectObligations()
    {
        var plan = Plan(Impact([Cycle("App")], rulesPresent: false), Policy(ArchitecturePolicyMode.Advisory));

        var p011 = Assert.Single(plan.Obligations.Where(item => item.RuleId == "P011" && item.SubjectId == "App"));
        Assert.Equal("App", p011.SubjectId);
        Assert.False(p011.Required);
    }

    [Fact]
    public void Planner_DeduplicatesViolationsBySubject_WithBothKindsInReasons()
    {
        var plan = Plan(Impact(
        [
            Layer("App.Shared"),
            new ArchitectureViolationRef("layer", "ui must not reference domain: App.View -> App.Shared", "App.Shared"),
            FanOut("App.Shared")
        ]));

        var p011 = Assert.Single(plan.Obligations.Where(item => item.RuleId == "P011" && item.SubjectId == "App.Shared"));
        Assert.Equal("App.Shared", p011.SubjectId);
        Assert.True(p011.Required);
        Assert.Contains("layer", p011.Reasons, StringComparer.Ordinal);
        Assert.Contains("fan-out", p011.Reasons, StringComparer.Ordinal);
        Assert.Equal(2, p011.Reasons.Count);
    }

    [Fact]
    public void Planner_RequiredCleanRun_EmitsRequiredRepositoryP011()
    {
        var plan = Plan(Impact([], rulesPresent: true));
        var p011 = Assert.Single(plan.Obligations.Where(item => item.RuleId == "P011"));
        Assert.Equal("architecture-rules", p011.SubjectId);
        Assert.True(p011.Required);
    }

    // --- 프로듀서 ---

    [Fact]
    public async Task Producer_ViolatingSubject_EmitsFailEvidence()
    {
        var obligation = P011("App.OrderService.Cancel");
        var plan = new ProofPlan([obligation], SourceDigest: "src");
        var impact = Impact([Layer("App.OrderService.Cancel")]);

        var evidence = await new ArchitectureEvidenceProducer().AnalyzeAsync(
            new ChangeRequest(".", "base", "head", [], SourceDigest: "src"), impact, plan, CancellationToken.None);

        var item = Assert.Single(evidence);
        Assert.Equal(EvidenceKind.Architecture, item.Kind);
        Assert.Equal(EvidenceStatus.Fail, item.Status);
        Assert.Equal(ArchitectureEvidenceProducer.CheckId, item.Provenance.CheckId);
        Assert.Equal("src", item.Provenance.SourceDigest);
        Assert.Equal("App.OrderService.Cancel", item.Subject);
        Assert.NotNull(item.Scope?.SubjectRefs);
        Assert.Contains("ui must not reference domain", item.Scope!.Subjects![0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Producer_ArchitectureRulesSubject_EmitsInconclusive()
    {
        var obligation = new ProofObligation(
            "O1", "P011", ObligationKind.Architecture, "rules", "architecture-rules", true, 3, ["r"],
            new ProofSubject(SubjectKind.Repository, "architecture-rules"));
        var plan = new ProofPlan([obligation], SourceDigest: "src");

        var evidence = await new ArchitectureEvidenceProducer().AnalyzeAsync(
            new ChangeRequest(".", "base", "head", [], SourceDigest: "src"), Impact(null), plan, CancellationToken.None);

        var item = Assert.Single(evidence);
        Assert.Equal(EvidenceStatus.Inconclusive, item.Status);
    }

    [Fact]
    public async Task Producer_CleanSubject_EmitsPassEvidence()
    {
        var plan = new ProofPlan([P011("App.Clean")], SourceDigest: "src");

        var evidence = await new ArchitectureEvidenceProducer().AnalyzeAsync(
            new ChangeRequest(".", "base", "head", [], SourceDigest: "src"), Impact([Cycle()]), plan, CancellationToken.None);

        var item = Assert.Single(evidence);
        Assert.Equal(EvidenceStatus.Pass, item.Status);
        Assert.Equal("rules-sha256", item.Provenance.PolicyDigest);
    }

    [Fact]
    public async Task Producer_RepositoryPolicyPassesOnlyAfterCleanCompletedAnalysis()
    {
        var plan = Plan(Impact([], rulesPresent: true));
        var evidence = await new ArchitectureEvidenceProducer().AnalyzeAsync(
            new ChangeRequest(".", "base", "head", [], SourceDigest: "src"),
            Impact([], rulesPresent: true), plan, CancellationToken.None);

        var item = Assert.Single(evidence);
        Assert.Equal("architecture-rules", item.Subject);
        Assert.Equal(EvidenceStatus.Pass, item.Status);
        Assert.Equal("rules-sha256", item.Provenance.PolicyDigest);

        var bound = new EvidenceBinder().Bind(plan, evidence);
        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, bound);
        Assert.Equal(ObligationStatus.Proven,
            Assert.Single(evaluation.Obligations, item => item.Obligation.SubjectId == "architecture-rules").Status);
    }

    [Fact]
    public async Task Producer_RepositoryPolicyFailsWhenCycleExists()
    {
        var impact = Impact([Cycle()], rulesPresent: true);
        var plan = Plan(impact);
        var obligation = Assert.Single(plan.Obligations, item => item.SubjectId == "architecture-rules");
        var evidence = await new ArchitectureEvidenceProducer().AnalyzeAsync(
            new ChangeRequest(".", "base", "head", [], SourceDigest: "src"), impact, plan, CancellationToken.None);

        var item = Assert.Single(evidence, item => item.Subject == obligation.SubjectId);
        Assert.Equal(EvidenceStatus.Fail, item.Status);
        var bound = new EvidenceBinder().Bind(plan, evidence);
        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, bound);
        Assert.NotEqual(ProofVerdict.Proven, evaluation.Verdict);
    }

    [Fact]
    public async Task Producer_EmptyButUncompletedAnalysisNeverPasses()
    {
        var impact = Impact(null, rulesPresent: true);
        var obligation = new ProofObligation(
            "O1", "P011", ObligationKind.Architecture, "rules", "architecture-rules", true, 3, ["r"],
            new ProofSubject(SubjectKind.Repository, "architecture-rules"));
        var evidence = await new ArchitectureEvidenceProducer().AnalyzeAsync(
            new ChangeRequest(".", "base", "head", [], SourceDigest: "src"), impact,
            new ProofPlan([obligation], SourceDigest: "src"), CancellationToken.None);

        Assert.Equal(EvidenceStatus.Inconclusive, Assert.Single(evidence).Status);
    }

    // --- 러너 ---

    [Fact]
    public async Task Runner_MalformedRulesJson_ReturnsNotRun()
    {
        var root = Path.Combine(Path.GetTempPath(), "p011-runner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".codemap"));
        try
        {
            File.WriteAllText(Path.Combine(root, ".codemap", "architecture.json"), "{ not valid json");

            var (violations, rulesPresent, rulesDigest) = await ArchitectureCheckRunner.RunAsync(
                root, Path.Combine(root, "missing", "index.db"), CancellationToken.None);

            Assert.Null(violations);
            Assert.True(rulesPresent);
            Assert.NotNull(rulesDigest);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // --- 바인더 ---

    [Fact]
    public void Binder_P008SarifEvidence_DoesNotBindP011()
    {
        var plan = new ProofPlan([P011("App")], SourceDigest: "src");
        var sarif = new ProofEvidence(
            "S1",
            EvidenceKind.StaticAnalysis,
            "App",
            EvidenceStatus.Pass,
            new EvidenceProvenance("distill", CheckId: "analyzers", SourceDigest: "src"),
            new EvidenceScope(
                ScopeMode.RepositoryWide,
                CommandTarget: "src/Proof.slnx",
                SubjectRefs: [new EvidenceSubjectRef(SubjectKind.Project, "App", "App")]));

        Assert.Null(EvidenceBinder.Match(plan.Obligations[0], sarif));
        Assert.Empty(new EvidenceBinder().Bind(plan, [sarif]).Links);
    }

    [Fact]
    public void Binder_BuildEvidence_DoesNotBindP011()
    {
        var plan = new ProofPlan([P011("App")], SourceDigest: "src");
        var build = new ProofEvidence(
            "B1",
            EvidenceKind.Build,
            "App",
            EvidenceStatus.Pass,
            new EvidenceProvenance("distill", CheckId: "build", SourceDigest: "src"),
            new EvidenceScope(ScopeMode.Exact, CommandTarget: "src/App/App.csproj"));

        Assert.Null(EvidenceBinder.Match(plan.Obligations[0], build));
    }

    [Fact]
    public void Binder_SubjectMatchedArchitectureEvidence_BindsDirect()
    {
        var plan = new ProofPlan([P011("App.OrderService.Cancel")], SourceDigest: "src");
        var bound = new EvidenceBinder().Bind(plan, [ArchitectureEvidence("App.OrderService.Cancel", EvidenceStatus.Pass)]);

        var link = Assert.Single(bound.Links);
        Assert.Equal("direct", link.Relation);
        Assert.Equal(3, link.Strength);
        Assert.Equal("BIND_ARCHITECTURE", link.BindingRuleId);
    }

    [Fact]
    public void Binder_DifferentSubject_ArchitectureEvidence_DoesNotBind()
    {
        var plan = new ProofPlan([P011("App.OrderService.Cancel")], SourceDigest: "src");
        Assert.Null(EvidenceBinder.Match(
            plan.Obligations[0],
            ArchitectureEvidence("App.Other", EvidenceStatus.Pass)));
    }

    [Fact]
    public void Binder_MissingSourceDigest_IsRejected()
    {
        var plan = new ProofPlan([P011("App.OrderService.Cancel")], SourceDigest: "src");
        var bound = new EvidenceBinder().Bind(
            plan,
            [ArchitectureEvidence("App.OrderService.Cancel", EvidenceStatus.Pass, sourceDigest: null)]);

        var link = Assert.Single(bound.Links);
        Assert.Equal("rejected", link.Relation);
        Assert.Equal(ProofReasonCodes.EvidenceSourceIdentityMissing, link.ReasonCode);
    }

    [Fact]
    public void Binder_PlannedArchitectureCheck_AdmitsEvidence_UnknownCheckIsRejected()
    {
        var plan = new ProofPlan([P011("App.OrderService.Cancel")], SourceDigest: "src");
        var evidence = ArchitectureEvidence("App.OrderService.Cancel", EvidenceStatus.Pass);
        var withCheck = new VerificationPlan(
            [new PlannedVerificationCheck("architecture", "architecture", string.Empty)],
            [], "quick");

        var admitted = new EvidenceBinder().Bind(plan, [evidence], withCheck);
        var link = Assert.Single(admitted.Links);
        Assert.Equal("direct", link.Relation);

        var withoutCheck = new VerificationPlan(
            [new PlannedVerificationCheck("build", "build", string.Empty)],
            [], "quick");
        var rejected = new EvidenceBinder().Bind(plan, [evidence], withoutCheck);
        Assert.Empty(rejected.Links);
    }

    // --- VerificationPlanner ---

    [Fact]
    public void VerificationPlanner_P011_SelectsArchitectureCapability_WithoutAnalyzersClones()
    {
        var plan = new ProofPlan([P011("App.OrderService.Cancel")], SourceDigest: "src");
        var catalog = DistillVerificationRunner.MergeProducerCapabilities(
        [
            new EvidenceCapability("build", "build", "src/Proof.slnx", ScopeMode.RepositoryWide, Cost: 2),
            new EvidenceCapability("analyzers", "analysis", "src/Proof.slnx", ScopeMode.RepositoryWide, Cost: 3)
        ], "quick");

        var verificationPlan = new VerificationPlanner().Plan(plan, catalog, "quick");

        Assert.DoesNotContain(verificationPlan.Uncovered, item => item.ObligationId == plan.Obligations[0].Id);
        Assert.Contains(verificationPlan.Checks, item => item.CheckId == "architecture");
        Assert.DoesNotContain(verificationPlan.Checks, item => item.CheckId.StartsWith("analyzers::", StringComparison.Ordinal));
    }

    [Fact]
    public void VerificationPlanner_P011_WithoutCapability_IsUncovered()
    {
        var plan = new ProofPlan([P011("App.OrderService.Cancel")], SourceDigest: "src");
        var catalog = new EvidenceCapability[]
        {
            new("build", "build", "src/Proof.slnx", ScopeMode.RepositoryWide, Cost: 2)
        };

        var verificationPlan = new VerificationPlanner().Plan(plan, catalog, "quick");

        var uncovered = Assert.Single(verificationPlan.Uncovered);
        Assert.Equal(plan.Obligations[0].Id, uncovered.ObligationId);
        Assert.Equal(ProofReasonCodes.RequiredEvidenceMissing, uncovered.Reason);
    }

    // --- 평가기 / 판정 ---

    [Fact]
    public void Evaluator_RequiredP011WithFailEvidence_VerdictNotReady()
    {
        var plan = new ProofPlan([P011("App.OrderService.Cancel")], SourceDigest: "src");
        var bound = new EvidenceBinder().Bind(plan, [ArchitectureEvidence("App.OrderService.Cancel", EvidenceStatus.Fail)]);

        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, bound);

        Assert.Equal(ObligationStatus.Failed, evaluation.Obligations[0].Status);
        Assert.Equal(ProofVerdict.NotReady, evaluation.Verdict);
    }

    [Fact]
    public void Evaluator_ArchitectureRulesInconclusive_IsUnresolvedVerdictUncertain()
    {
        var obligation = new ProofObligation(
            "O1", "P011", ObligationKind.Architecture, "rules", "architecture-rules", true, 3, ["r"],
            new ProofSubject(SubjectKind.Repository, "architecture-rules"));
        var plan = new ProofPlan([obligation], SourceDigest: "src");
        var bound = new EvidenceBinder().Bind(
            plan,
            [ArchitectureEvidence("architecture-rules", EvidenceStatus.Inconclusive, subjectKind: SubjectKind.Repository)]);

        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, bound);

        Assert.Equal(ObligationStatus.Unresolved, evaluation.Obligations[0].Status);
        Assert.Equal(ProofVerdict.Uncertain, evaluation.Verdict);
        Assert.NotEqual(ProofVerdict.Proven, evaluation.Verdict);
    }

    [Fact]
    public void Evaluator_AdvisoryP011WithFailEvidence_NeverBlocks()
    {
        var plan = new ProofPlan([P011("App.OrderService.Cancel", required: false)], SourceDigest: "src");
        var bound = new EvidenceBinder().Bind(plan, [ArchitectureEvidence("App.OrderService.Cancel", EvidenceStatus.Fail)]);

        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, bound);

        Assert.Equal(ObligationStatus.Failed, evaluation.Obligations[0].Status);
        Assert.NotEqual(ProofVerdict.NotReady, evaluation.Verdict);
    }
}

public sealed class RequiredP011CertificateTests
{
    [Fact]
    public void LatestCertificate_ProvesRepositoryP011WithDirectArchitectureEvidence()
    {
        var certificateDirectory = Path.Combine(FindRepositoryRoot(), ".proof", "certificates");
        Assert.True(Directory.Exists(certificateDirectory), "Proof verify did not produce a certificate directory.");

        var certificatePath = Directory.GetFiles(certificateDirectory, "*.json")
            .Where(path => !path.EndsWith(".summary.json", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        Assert.NotNull(certificatePath);

        using var certificate = JsonDocument.Parse(File.ReadAllText(certificatePath));
        var root = certificate.RootElement;
        var obligations = root.GetProperty("evaluation").GetProperty("obligations");
        var requiredP011 = obligations.EnumerateArray()
            .Where(item =>
            {
                var obligation = item.GetProperty("obligation");
                return obligation.GetProperty("ruleId").GetString() == "P011"
                       && obligation.GetProperty("required").GetBoolean()
                       && obligation.GetProperty("subjectId").GetString() == "architecture-rules";
            })
            .ToArray();

        var evaluated = Assert.Single(requiredP011);
        Assert.Equal("PROVEN", evaluated.GetProperty("status").GetString());
        var obligationId = evaluated.GetProperty("obligation").GetProperty("id").GetString();
        var evidence = root.GetProperty("evidence");
        var evidenceById = evidence.GetProperty("evidence").EnumerateArray()
            .ToDictionary(item => item.GetProperty("id").GetString()!, item => item);
        var sourceDigest = root.GetProperty("sourceDigest").GetString();
        var directEvidence = evidence.GetProperty("links").EnumerateArray()
            .Where(link =>
                link.GetProperty("obligationId").GetString() == obligationId
                && link.GetProperty("relation").GetString() == "direct"
                && link.GetProperty("strength").GetDouble() >= 3
                && link.GetProperty("bindingRuleId").GetString() == "BIND_ARCHITECTURE")
            .Select(link => evidenceById[link.GetProperty("evidenceId").GetString()!])
            .Where(item =>
            {
                var provenance = item.GetProperty("provenance");
                return item.GetProperty("kind").GetString() == "ARCHITECTURE"
                       && item.GetProperty("status").GetString() == "PASS"
                       && provenance.GetProperty("checkId").GetString() == "architecture"
                       && provenance.GetProperty("sourceDigest").GetString() == sourceDigest
                       && provenance.TryGetProperty("policyDigest", out var policyDigest)
                       && !string.IsNullOrWhiteSpace(policyDigest.GetString());
            })
            .ToArray();

        Assert.Single(directEvidence);
    }

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "Proof.slnx")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the Proofmark repository root from the test output directory.");
    }
}
