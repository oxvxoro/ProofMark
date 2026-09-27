using Proof.Cli;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

[Collection("AttestationEnvironment")]
public sealed class AttestationIdentityTests
{
    private const string Key = "test-key";

    private static AttestationContext CiContext() => new(
        "run-1",
        "owner/repo",
        "main",
        "refs/heads/main",
        "owner/repo/.github/workflows/ci.yml@refs/heads/main",
        "workflow-sha",
        "commit-sha",
        "2");

    [Fact]
    public async Task Signer_records_ci_identity_claims()
    {
        var envelope = await SignAsync(CiContext());

        Assert.Equal("owner/repo", envelope.Claims?["repository"]);
        Assert.Equal("refs/heads/main", envelope.Claims?["ref"]);
        Assert.Equal("owner/repo/.github/workflows/ci.yml@refs/heads/main", envelope.Claims?["workflowRef"]);
        Assert.Equal("workflow-sha", envelope.Claims?["workflowSha"]);
        Assert.Equal("commit-sha", envelope.Claims?["commitSha"]);
        Assert.Equal("2", envelope.Claims?["runAttempt"]);
    }

    [Fact]
    public async Task Verifier_accepts_matching_identity()
    {
        var envelope = await SignAsync(CiContext());
        var result = await VerifyAsync(
            envelope,
            new TrustPolicy(
                RequireSigned: true,
                Repository: "owner/repo",
                AllowedRefs: ["refs/heads/main"],
                AllowedWorkflows: ["owner/repo/.github/workflows/ci.yml@refs/heads/main"],
                ExpectedCommitSha: "commit-sha"));

        Assert.True(result.SignatureValid);
        Assert.True(result.Trusted);
    }

    [Fact]
    public async Task Verifier_rejects_repository_mismatch_without_invalidating_signature()
    {
        var envelope = await SignAsync(CiContext());
        var result = await VerifyAsync(envelope, new TrustPolicy(RequireSigned: true, Repository: "other/repo"));

        Assert.True(result.SignatureValid);
        Assert.False(result.Trusted);
        Assert.Contains("repository", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Verifier_rejects_workflow_mismatch()
    {
        var envelope = await SignAsync(CiContext());
        var result = await VerifyAsync(
            envelope,
            new TrustPolicy(RequireSigned: true, AllowedWorkflows: ["owner/repo/.github/workflows/other.yml@refs/heads/main"]));

        Assert.False(result.Trusted);
        Assert.Contains("workflow", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Verifier_rejects_missing_commit_claim_when_expected()
    {
        var envelope = await SignAsync(new AttestationContext("run-1"));
        var result = await VerifyAsync(envelope, new TrustPolicy(RequireSigned: true, ExpectedCommitSha: "commit-sha"));

        Assert.True(result.SignatureValid);
        Assert.False(result.Trusted);
        Assert.Contains("commit", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Verifier_falls_back_to_branch_claim_for_allowed_refs()
    {
        // 레거시 봉투에는 브랜치 클레임만 있다(ref 없음). 허용 목록은
        // 옛 인증서가 계속 검증되도록 여전히 그것과 일치해야 한다.
        var envelope = await SignAsync(new AttestationContext("run-1", "owner/repo", "main"));
        var result = await VerifyAsync(envelope, new TrustPolicy(RequireSigned: true, AllowedRefs: ["main"]));

        Assert.True(result.Trusted);
    }

    private static async Task<AttestationEnvelope> SignAsync(AttestationContext context)
    {
        Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, Key);
        try
        {
            return await new HmacAttestationSigner().SignAsync("digest", context, CancellationToken.None);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, null);
        }
    }

    private static async Task<AttestationVerificationResult> VerifyAsync(AttestationEnvelope envelope, TrustPolicy policy)
    {
        Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, Key);
        try
        {
            return await new HmacAttestationVerifier().VerifyAsync(envelope, policy, CancellationToken.None);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, null);
        }
    }

    [Fact]
    public void CertificateVerify_identity_constraint_without_attestation_fails()
    {
        var path = WriteCertificate(BuildCertificate());
        try
        {
            Assert.Equal(1, CertificateVerifyCommand.Verify(path, repository: "owner/repo"));
            Assert.Equal(1, CertificateVerifyCommand.Verify(path, expectedCommitSha: "commit-sha"));
            Assert.Equal(0, CertificateVerifyCommand.Verify(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CertificateVerify_enforces_identity_claims()
    {
        var certificate = BuildCertificate();
        Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, Key);
        try
        {
            var envelope = await new HmacAttestationSigner().SignAsync(
                certificate.StatementDigest!, CiContext(), CancellationToken.None);
            var path = WriteCertificate(certificate with { Attestation = envelope });
            try
            {
                Assert.Equal(0, CertificateVerifyCommand.Verify(
                    path,
                    requireSigned: true,
                    repository: "owner/repo",
                    allowedRefs: ["refs/heads/main"],
                    allowedWorkflows: ["owner/repo/.github/workflows/ci.yml@refs/heads/main"],
                    expectedCommitSha: "commit-sha"));
                Assert.Equal(1, CertificateVerifyCommand.Verify(path, requireSigned: true, repository: "other/repo"));
            }
            finally
            {
                File.Delete(path);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, null);
        }
    }

    private static ChangeCertificate BuildCertificate()
    {
        var impact = new ChangeImpact("abc", "def", [], [], [], [], [], "complete", false, SourceDigest: "src");
        var plan = new ProofPlan([], SourceDigest: "src");
        return new ChangeCertificateBuilder().Build(
            impact, plan, new VerificationEvidenceSet([], []), new ProofEvaluation(ProofVerdict.NoChange, []));
    }

    private static string WriteCertificate(ChangeCertificate certificate)
    {
        var path = Path.Combine(Path.GetTempPath(), "proof-identity-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(certificate, ProofJson.WireOptions));
        return path;
    }
}
