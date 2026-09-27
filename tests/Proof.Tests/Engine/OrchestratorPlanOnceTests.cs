using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

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
