using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

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
