using CodeMap.CSharp.Analysis;
using Proof.Adapters.CodeMap;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

public sealed class ArchitectureBoundaryTests
{
    [Fact]
    public void EngineProject_DoesNotReferenceDistillGit()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Proof.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var csproj = Path.Combine(dir!.FullName, "src", "Proof.Engine", "Proof.Engine.csproj");
        Assert.True(File.Exists(csproj), csproj);
        var text = File.ReadAllText(csproj);
        Assert.DoesNotContain("Distill.Git", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CliProject_DoesNotReferenceEnginesDirectly()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Proof.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var csproj = Path.Combine(dir!.FullName, "src", "Proof.Cli", "Proof.Cli.csproj");
        Assert.True(File.Exists(csproj), csproj);
        var text = File.ReadAllText(csproj);
        Assert.DoesNotContain("engines\\", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("engines/", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GitProject_DoesNotReferenceEngine()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Proof.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var csproj = Path.Combine(dir!.FullName, "src", "Proof.Adapters.Git", "Proof.Adapters.Git.csproj");
        Assert.True(File.Exists(csproj), csproj);
        var text = File.ReadAllText(csproj);
        Assert.DoesNotContain("Proof.Engine", text, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class OrchestratorPlanOnceTests
{
    [Fact]
    public async Task VerifyAsync_InvokesRunnerOnceWithVerificationPlan()
    {
        var runner = new CountingRunner();
        var orchestrator = new ProofOrchestrator(
            new StaticImpactProvider(),
            new DeterministicProofPlanner(),
            runner,
            new DeterministicProofEvaluator(),
            new ChangeCertificateBuilder());

        var request = new ChangeRequest("root", "base", "head", [], ChangeSetIsEmpty: true, SourceDigest: "src");
        var certificate = await orchestrator.VerifyAsync(
            request,
            "quick",
            CancellationToken.None,
            capabilities: [
                new EvidenceCapability("build", "build", "App.slnx", ScopeMode.RepositoryWide)
            ]);

        Assert.Equal(1, runner.Calls);
        Assert.NotNull(runner.LastPlan);
        Assert.Equal("quick", runner.LastPlan!.Profile);
        Assert.Equal(runner.LastPlan.DefinitionDigest, certificate.VerificationPlan?.DefinitionDigest);
    }

    private sealed class CountingRunner : IVerificationRunner
    {
        public int Calls { get; private set; }
        public VerificationPlan? LastPlan { get; private set; }

        public Task<VerificationRunResult> VerifyAsync(
            ProofPlan plan,
            VerificationPlan verificationPlan,
            string workspaceRoot,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastPlan = verificationPlan;
            return Task.FromResult(new VerificationRunResult(new VerificationEvidenceSet([], []), []));
        }
    }

    private sealed class StaticImpactProvider : IChangeImpactProvider
    {
        public Task<ChangeImpact> AnalyzeAsync(ChangeRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ChangeImpact(
                request.BaseRevision,
                request.HeadRevision,
                request.Spans,
                [],
                [],
                [],
                [],
                "complete",
                false,
                SourceDigest: request.SourceDigest));
    }
}

public sealed class ApiCompatibilityProducerTests
{
    [Fact]
    public async Task AnalyzeAsync_FingerprintMatch_IsPass()
    {
        var facts = CodeMapPublicSurfaceAnalyzer.Analyze(
            new Dictionary<string, string?> { ["App"] = "abc" },
            new Dictionary<string, string?> { ["App"] = "abc" },
            new Dictionary<string, IReadOnlyList<PublicSurfaceEntry>>(),
            new Dictionary<string, IReadOnlyList<PublicSurfaceEntry>>());
        var producer = new ApiCompatibilityEvidenceProducer(facts);
        var plan = new ProofPlan([
            new ProofObligation("O1", "P001A", ObligationKind.Compatibility, "api", "s1", true, 4, ["r"], new ProofSubject(SubjectKind.ApiSurface, "s1", "App"))
        ]);
        var evidence = await producer.AnalyzeAsync(new ChangeRequest("root", "b", "h", [], SourceDigest: "d"), plan, CancellationToken.None);
        Assert.Equal(EvidenceStatus.Pass, evidence[0].Status);
        Assert.Equal(EvidenceKind.ApiCompatibility, evidence[0].Kind);
    }

    [Fact]
    public async Task AnalyzeAsync_AdditiveSurface_IsPass()
    {
        var baseline = new[] { new PublicSurfaceEntry("Method", "Lib.Foo", "void Foo()", "public") };
        var head = new[]
        {
            new PublicSurfaceEntry("Method", "Lib.Foo", "void Foo()", "public"),
            new PublicSurfaceEntry("Method", "Lib.Bar", "void Bar()", "public")
        };
        var facts = CodeMapPublicSurfaceAnalyzer.Analyze(
            new Dictionary<string, string?> { ["App"] = "old" },
            new Dictionary<string, string?> { ["App"] = "new" },
            new Dictionary<string, IReadOnlyList<PublicSurfaceEntry>> { ["App"] = baseline },
            new Dictionary<string, IReadOnlyList<PublicSurfaceEntry>> { ["App"] = head });
        var producer = new ApiCompatibilityEvidenceProducer(facts);
        var plan = new ProofPlan([
            new ProofObligation("O1", "P001A", ObligationKind.Compatibility, "api", "s1", true, 4, ["r"], new ProofSubject(SubjectKind.ApiSurface, "s1", "App"))
        ]);
        var evidence = await producer.AnalyzeAsync(new ChangeRequest("root", "b", "h", [], SourceDigest: "d"), plan, CancellationToken.None);
        Assert.Equal(EvidenceStatus.Pass, evidence[0].Status);
        var bound = new EvidenceBinder().Bind(plan, evidence, new VerificationPlan(
            [new PlannedVerificationCheck("api-compatibility", "apicompatibility", string.Empty)],
            [],
            "quick"));
        Assert.Equal(ProofVerdict.Proven, new DeterministicProofEvaluator().Evaluate(plan, bound).Verdict);
    }

    [Fact]
    public async Task AnalyzeAsync_Removal_IsFail()
    {
        var baseline = new[] { new PublicSurfaceEntry("Method", "Lib.Foo", "void Foo()", "public") };
        var facts = CodeMapPublicSurfaceAnalyzer.Analyze(
            new Dictionary<string, string?> { ["App"] = "old" },
            new Dictionary<string, string?> { ["App"] = "new" },
            new Dictionary<string, IReadOnlyList<PublicSurfaceEntry>> { ["App"] = baseline },
            new Dictionary<string, IReadOnlyList<PublicSurfaceEntry>> { ["App"] = [] });
        var producer = new ApiCompatibilityEvidenceProducer(facts);
        var plan = new ProofPlan([
            new ProofObligation("O1", "P001A", ObligationKind.Compatibility, "api", "s1", true, 4, ["r"], new ProofSubject(SubjectKind.ApiSurface, "s1", "App"))
        ]);
        var evidence = await producer.AnalyzeAsync(new ChangeRequest("root", "b", "h", [], SourceDigest: "d"), plan, CancellationToken.None);
        Assert.Equal(EvidenceStatus.Fail, evidence[0].Status);
    }

    [Fact]
    public async Task AnalyzeAsync_CallFacts_DoNotReuseConstructorFacts()
    {
        var producer = new ApiCompatibilityEvidenceProducer(
            [new ApiCompatibilityFact("App", ApiCompatibilityState.Unchanged)]);
        var plan = new ProofPlan([
            new ProofObligation("O1", "P001A", ObligationKind.Compatibility, "api", "s1", true, 4, ["r"], new ProofSubject(SubjectKind.ApiSurface, "s1", "App"))
        ]);
        var request = new ChangeRequest("root", "b", "h", [], SourceDigest: "d");
        var breaking = await producer.AnalyzeAsync(
            request,
            plan,
            [new ApiCompatibilityFact("App", ApiCompatibilityState.Breaking)],
            CancellationToken.None);
        var unchanged = await producer.AnalyzeAsync(request, plan, CancellationToken.None);
        Assert.Equal(EvidenceStatus.Fail, breaking[0].Status);
        Assert.Equal(EvidenceStatus.Pass, unchanged[0].Status);
    }

    [Fact]
    public void AnalyzeAsync_FingerprintMismatchWithoutEntries_IsInconclusive()
    {
        Assert.Equal(
            EvidenceStatus.Inconclusive,
            Proof.Adapters.CodeMap.ApiCompatibilityEvidenceProducer.ResolveStatus("a", "b"));
    }
}

[Collection("AttestationEnvironment")]
public sealed class AttestationTests
{
    [Fact]
    public async Task UnsignedVerifier_RejectsWhenSignedRequired()
    {
        var signer = new HmacAttestationSigner();
        var envelope = await signer.SignAsync("digest", new AttestationContext("run"), CancellationToken.None);
        var result = await new HmacAttestationVerifier().VerifyAsync(envelope, new TrustPolicy(RequireSigned: true), CancellationToken.None);
        Assert.False(result.Trusted);
    }
}

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

[Collection("AttestationEnvironment")]
public sealed class FreshnessTests
{
    private const string HmacKey = "freshness-test-hmac-key";

    [Fact]
    public async Task VerifyFromWorkspace_SourceDrift_IsUncertain()
    {
        var collector = new DriftCollector();
        var orchestrator = new ProofOrchestrator(
            new StaticImpactProvider(),
            new DeterministicProofPlanner(),
            new EmptyRunner(),
            new DeterministicProofEvaluator(),
            new ChangeCertificateBuilder(),
            snapshotCollector: collector);
        var certificate = await orchestrator.VerifyFromWorkspaceAsync("root", "base", "head", "quick", CancellationToken.None);
        Assert.Equal(ProofVerdict.Uncertain, certificate.Verdict);
        Assert.Equal(ProofReasonCodes.SourceFreshnessDrift, certificate.Evaluation.ReasonCode);
    }

    [Fact]
    public async Task VerifyFromWorkspace_SourceDrift_WithHmacKey_AttestationMatchesDriftedStatement()
    {
        Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, HmacKey);
        try
        {
            var collector = new DriftCollector();
            var orchestrator = new ProofOrchestrator(
                new StaticImpactProvider(),
                new DeterministicProofPlanner(),
                new EmptyRunner(),
                new DeterministicProofEvaluator(),
                new ChangeCertificateBuilder(),
                snapshotCollector: collector,
                attestationSigner: new HmacAttestationSigner());
            var certificate = await orchestrator.VerifyFromWorkspaceAsync(
                "root", "base", "head", "quick", CancellationToken.None);
            Assert.Equal(ProofVerdict.Uncertain, certificate.Verdict);
            Assert.Equal(ProofReasonCodes.SourceFreshnessDrift, certificate.Evaluation.ReasonCode);
            Assert.NotNull(certificate.Attestation);
            Assert.Equal("hmac-sha256", certificate.Attestation.Provider);
            Assert.Equal(certificate.StatementDigest, certificate.Attestation.StatementDigest);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, null);
        }
    }

    [Fact]
    public async Task VerifyFromWorkspace_SourceDrift_WithoutHmacKey_AttestationMatchesDriftedStatement()
    {
        Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, null);
        var collector = new DriftCollector();
        var orchestrator = new ProofOrchestrator(
            new StaticImpactProvider(),
            new DeterministicProofPlanner(),
            new EmptyRunner(),
            new DeterministicProofEvaluator(),
            new ChangeCertificateBuilder(),
            snapshotCollector: collector,
            attestationSigner: new HmacAttestationSigner());
        var certificate = await orchestrator.VerifyFromWorkspaceAsync(
            "root", "base", "head", "quick", CancellationToken.None);
        Assert.Equal(ProofVerdict.Uncertain, certificate.Verdict);
        Assert.NotNull(certificate.Attestation);
        Assert.Equal("none", certificate.Attestation.Provider);
        Assert.Equal(certificate.StatementDigest, certificate.Attestation.StatementDigest);
    }

    private sealed class DriftCollector : ISourceSnapshotCollector
    {
        private int _calls;

        public Task<SourceSnapshot> CollectAsync(string workspaceRoot, string baseRevision, string headRevision, CancellationToken cancellationToken)
        {
            _calls++;
            var digest = _calls == 1 ? "first" : "second";
            return Task.FromResult(new SourceSnapshot(workspaceRoot, baseRevision, headRevision, true, digest, [], true));
        }
    }

    private sealed class EmptyRunner : IVerificationRunner
    {
        public Task<VerificationRunResult> VerifyAsync(ProofPlan plan, VerificationPlan verificationPlan, string workspaceRoot, CancellationToken cancellationToken)
            => Task.FromResult(new VerificationRunResult(new VerificationEvidenceSet([], []), []));
    }

    private sealed class StaticImpactProvider : IChangeImpactProvider
    {
        public Task<ChangeImpact> AnalyzeAsync(ChangeRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ChangeImpact(
                request.BaseRevision,
                request.HeadRevision,
                request.Spans,
                [],
                [],
                [],
                [],
                "complete",
                false,
                SourceDigest: request.SourceDigest));
    }
}
