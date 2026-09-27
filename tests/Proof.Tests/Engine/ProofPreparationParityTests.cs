using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

public sealed class ProofPreparationParityTests
{
    [Fact]
    public async Task PlanAsync_And_VerifyAsync_PropagateUsedFileWideFallbackConsistently()
    {
        var harness = CreateHarness(flags: false);
        var request = Request(usedFileWideFallback: true);

        var (impact, plan, _) = await harness.Orchestrator.PlanAsync(request, "quick", CancellationToken.None);
        var certificate = await harness.Orchestrator.VerifyAsync(request, "quick", CancellationToken.None);

        Assert.True(impact.UsedFileWideFallback);
        Assert.True(certificate.Impact.UsedFileWideFallback);
        Assert.Contains(plan.Constraints ?? [], item => item.Code == ProofReasonCodes.FileWideFallback);
        Assert.Equal(Codes(plan), Codes(certificate.Plan));
    }

    [Fact]
    public async Task PlanAsync_And_VerifyAsync_PropagateCaptureFailuresConsistently()
    {
        var delta = new FileDelta(FileChangeKind.Modified, null, "src/App.cs", [], []);
        var harness = CreateHarness(flags: false);
        var request = Request(
            untrackedCaptureFailed: true,
            contentHashCaptureFailed: true,
            structuredDeltaFailed: true,
            fileDeltas: [delta]);

        var (impact, plan, _) = await harness.Orchestrator.PlanAsync(request, "quick", CancellationToken.None);
        var certificate = await harness.Orchestrator.VerifyAsync(request, "quick", CancellationToken.None);

        Assert.Equal(impact.UntrackedCaptureFailed, certificate.Impact.UntrackedCaptureFailed);
        Assert.Equal(impact.ContentHashCaptureFailed, certificate.Impact.ContentHashCaptureFailed);
        Assert.Equal(impact.StructuredDeltaFailed, certificate.Impact.StructuredDeltaFailed);
        Assert.True(impact.UntrackedCaptureFailed);
        Assert.True(impact.ContentHashCaptureFailed);
        Assert.True(impact.StructuredDeltaFailed);
        Assert.Equal(delta, Assert.Single(impact.FileDeltas ?? []));
        Assert.Equal(delta, Assert.Single(certificate.Impact.FileDeltas ?? []));
        Assert.Equal(Codes(plan), Codes(certificate.Plan));
        Assert.Contains(Codes(plan), code => code == ProofReasonCodes.ChangeCaptureUntrackedFailed);
        Assert.Contains(Codes(plan), code => code == ProofReasonCodes.ChangeCaptureContentHashFailed);
        Assert.Contains(Codes(plan), code => code == ProofReasonCodes.ChangeStructuredDeltaFailed);
    }

    [Fact]
    public async Task PlanAsync_WithNullCapabilities_DisablesCapabilityPlanning()
    {
        var harness = CreateHarness(flags: false, withRequiredObligation: true);
        var request = Request();

        var (_, plan, verificationPlan) = await harness.Orchestrator.PlanAsync(
            request, "quick", CancellationToken.None, capabilities: null);

        Assert.Null(verificationPlan);
        Assert.DoesNotContain(Codes(plan), code => code == ProofReasonCodes.RequiredEvidenceCapabilityMissing);
    }

    [Fact]
    public async Task VerifyAsync_WithNullCapabilities_DoesNotFabricateMissingCapabilityConstraints()
    {
        var harness = CreateHarness(flags: false, withRequiredObligation: true);
        var request = Request();

        var certificate = await harness.Orchestrator.VerifyAsync(
            request, "quick", CancellationToken.None, capabilities: null);

        Assert.Equal(1, harness.Runner.Calls);
        Assert.Empty(certificate.VerificationPlan?.Checks ?? []);
        Assert.DoesNotContain(Codes(certificate.Plan), code => code == ProofReasonCodes.RequiredEvidenceCapabilityMissing);
    }

    [Fact]
    public async Task EmptyCatalog_MaterializesUncoveredInBothFlows()
    {
        var harness = CreateHarness(flags: false, withRequiredObligation: true);
        var request = Request();
        IReadOnlyList<EvidenceCapability> catalog = [];

        var (_, plan, verificationPlan) = await harness.Orchestrator.PlanAsync(
            request, "quick", CancellationToken.None, capabilities: catalog);
        var certificate = await harness.Orchestrator.VerifyAsync(
            request, "quick", CancellationToken.None, capabilities: catalog);

        Assert.NotNull(verificationPlan);
        Assert.NotEmpty(verificationPlan.Uncovered);
        Assert.Contains(Codes(plan), code => code == ProofReasonCodes.RequiredEvidenceCapabilityMissing);
        Assert.Equal(Codes(plan), Codes(certificate.Plan));
        Assert.Equal(
            verificationPlan.Uncovered.Select(item => item.ObligationId).ToArray(),
            certificate.VerificationPlan!.Uncovered.Select(item => item.ObligationId).ToArray());
    }

    [Fact]
    public async Task ExplicitCatalog_ProducesIdenticalVerificationPlanInBothFlows()
    {
        var harness = CreateHarness(flags: false, withRequiredObligation: true);
        var request = Request(isDirty: true);
        IReadOnlyList<EvidenceCapability> catalog =
        [
            new EvidenceCapability("build", "build", "App.slnx", ScopeMode.RepositoryWide)
        ];

        var (impact, plan, verificationPlan) = await harness.Orchestrator.PlanAsync(
            request, "quick", CancellationToken.None, capabilities: catalog);
        var certificate = await harness.Orchestrator.VerifyAsync(
            request, "quick", CancellationToken.None, capabilities: catalog);

        Assert.NotNull(verificationPlan);
        Assert.Equal(verificationPlan.DefinitionDigest, certificate.VerificationPlan!.DefinitionDigest);
        Assert.Equal(verificationPlan.SourceDirty, certificate.VerificationPlan.SourceDirty);
        Assert.True(verificationPlan.SourceDirty);
        Assert.Equal(
            verificationPlan.Checks.Select(item => item.CheckId).ToArray(),
            certificate.VerificationPlan.Checks.Select(item => item.CheckId).ToArray());
        Assert.Equal(
            verificationPlan.Uncovered.Select(item => item.ObligationId).ToArray(),
            certificate.VerificationPlan.Uncovered.Select(item => item.ObligationId).ToArray());
        Assert.Equal(Codes(plan), Codes(certificate.Plan));
        Assert.Equal(impact.SourceDigest, certificate.Impact.SourceDigest);
    }

    private static string[] Codes(ProofPlan plan)
        => (plan.Constraints ?? []).Select(item => item.Code).ToArray();

    private static ChangeRequest Request(
        bool usedFileWideFallback = false,
        bool untrackedCaptureFailed = false,
        bool contentHashCaptureFailed = false,
        bool structuredDeltaFailed = false,
        IReadOnlyList<FileDelta>? fileDeltas = null,
        bool isDirty = false)
        => new(
            "root",
            "base",
            "head",
            [],
            SourceDigest: "src",
            UsedFileWideFallback: usedFileWideFallback,
            UntrackedCaptureFailed: untrackedCaptureFailed,
            FileDeltas: fileDeltas,
            ContentHashCaptureFailed: contentHashCaptureFailed,
            StructuredDeltaFailed: structuredDeltaFailed,
            IsDirty: isDirty);

    private static Harness CreateHarness(bool flags, bool withRequiredObligation = false)
    {
        var runner = new CountingRunner();
        var orchestrator = new ProofOrchestrator(
            new FlagImpactProvider(flags, withRequiredObligation),
            new DeterministicProofPlanner(),
            runner,
            new DeterministicProofEvaluator(),
            new ChangeCertificateBuilder());
        return new Harness(orchestrator, runner);
    }

    private sealed record Harness(ProofOrchestrator Orchestrator, CountingRunner Runner);

    private sealed class CountingRunner : IVerificationRunner
    {
        public int Calls { get; private set; }

        public Task<VerificationRunResult> VerifyAsync(
            ProofPlan plan,
            VerificationPlan verificationPlan,
            string workspaceRoot,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new VerificationRunResult(new VerificationEvidenceSet([], []), []));
        }
    }

    private sealed class FlagImpactProvider(bool flags, bool withRequiredObligation) : IChangeImpactProvider
    {
        public Task<ChangeImpact> AnalyzeAsync(ChangeRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ChangeImpact(
                request.BaseRevision,
                request.HeadRevision,
                request.Spans,
                [],
                withRequiredObligation
                    ?
                    [
                        new ImpactedSymbolRef(
                            "t1",
                            "Tests",
                            "t.cs",
                            "Tests.Sample",
                            "s1",
                            1,
                            true,
                            "Exact",
                            1)
                    ]
                    : [],
                [],
                [],
                "complete",
                false,
                SourceDigest: null,
                UsedFileWideFallback: flags,
                UntrackedCaptureFailed: flags,
                ContentHashCaptureFailed: flags,
                StructuredDeltaFailed: flags));
    }
}
