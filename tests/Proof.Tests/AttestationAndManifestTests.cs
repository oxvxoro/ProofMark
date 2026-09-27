using Proof.Cli;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

[Collection("AttestationEnvironment")]
public sealed class AttestationAndManifestTests
{
    [Fact]
    public async Task Signer_WithoutHmacKey_IsUnsigned_AndCarriesContextClaims()
    {
        Environment.SetEnvironmentVariable("PROOF_ATTESTATION_HMAC_KEY", null);
        var envelope = await new HmacAttestationSigner().SignAsync(
            "digest", new AttestationContext("run", "owner/repo", "main"), CancellationToken.None);

        Assert.Equal("none", envelope.Provider);
        Assert.Equal("owner/repo", envelope.Claims?["repository"]);
        Assert.Equal("main", envelope.Claims?["branch"]);
    }

    [Fact]
    public async Task Signer_WithHmacKey_ProducesSignatureWithoutLeakingKey()
    {
        Environment.SetEnvironmentVariable("PROOF_ATTESTATION_HMAC_KEY", "test-key");
        try
        {
            var envelope = await new HmacAttestationSigner().SignAsync(
                "digest", new AttestationContext("run"), CancellationToken.None);

            Assert.Equal("hmac-sha256", envelope.Provider);
            Assert.Equal("env:PROOF_ATTESTATION_HMAC_KEY", envelope.SignerIdentity);
            Assert.NotNull(envelope.Payload);
            Assert.DoesNotContain("test-key", envelope.Payload, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROOF_ATTESTATION_HMAC_KEY", null);
        }
    }

    [Fact]
    public async Task Verifier_RequireSigned_TrustsOnlyHmacProvider()
    {
        Environment.SetEnvironmentVariable("PROOF_ATTESTATION_HMAC_KEY", "test-key");
        try
        {
            var hmacEnvelope = await new HmacAttestationSigner().SignAsync(
                "digest", new AttestationContext("run"), CancellationToken.None);
            var unsignedEnvelope = new AttestationEnvelope("digest", "unsigned", "none");

            Assert.True((await new HmacAttestationVerifier()
                .VerifyAsync(hmacEnvelope, new TrustPolicy(RequireSigned: true), CancellationToken.None)).Trusted);
            Assert.False((await new HmacAttestationVerifier()
                .VerifyAsync(unsignedEnvelope, new TrustPolicy(RequireSigned: true), CancellationToken.None)).Trusted);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROOF_ATTESTATION_HMAC_KEY", null);
        }
    }

    [Fact]
    public async Task Verifier_RequireSigned_RejectsForgedHmacPayload()
    {
        Environment.SetEnvironmentVariable("PROOF_ATTESTATION_HMAC_KEY", "test-key");
        try
        {
            var forged = new AttestationEnvelope(
                "digest",
                "env:PROOF_ATTESTATION_HMAC_KEY",
                "hmac-sha256",
                Payload: "00");

            var result = await new HmacAttestationVerifier()
                .VerifyAsync(forged, new TrustPolicy(RequireSigned: true), CancellationToken.None);

            Assert.False(result.SignatureValid);
            Assert.False(result.Trusted);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROOF_ATTESTATION_HMAC_KEY", null);
        }
    }

    [Fact]
    public async Task Verifier_TrustedIssuers_AllowlistControlsTrust()
    {
        Environment.SetEnvironmentVariable("PROOF_ATTESTATION_HMAC_KEY", "test-key");
        try
        {
            var envelope = await new HmacAttestationSigner().SignAsync(
                "digest", new AttestationContext("run"), CancellationToken.None);
            var verifier = new HmacAttestationVerifier();

            var allowed = await verifier.VerifyAsync(
                envelope,
                new TrustPolicy(TrustedIssuers: ["env:PROOF_ATTESTATION_HMAC_KEY"]),
                CancellationToken.None);
            Assert.True(allowed.SignatureValid);
            Assert.True(allowed.Trusted);

            var denied = await verifier.VerifyAsync(
                envelope,
                new TrustPolicy(TrustedIssuers: ["some-other-issuer"]),
                CancellationToken.None);
            Assert.True(denied.SignatureValid);
            Assert.False(denied.Trusted);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROOF_ATTESTATION_HMAC_KEY", null);
        }
    }

    [Fact]
    public async Task Verifier_HmacButMissingKey_IsNotTrusted()
    {
        Environment.SetEnvironmentVariable("PROOF_ATTESTATION_HMAC_KEY", null);
        var envelope = new AttestationEnvelope(
            "digest",
            "env:PROOF_ATTESTATION_HMAC_KEY",
            "hmac-sha256",
            Payload: new string('a', 64));

        var result = await new HmacAttestationVerifier()
            .VerifyAsync(envelope, new TrustPolicy(RequireSigned: true), CancellationToken.None);

        Assert.False(result.SignatureValid);
        Assert.False(result.Trusted);
        Assert.Contains("key", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Verifier_MalformedHexPayload_IsRejectedWithoutThrowing()
    {
        Environment.SetEnvironmentVariable("PROOF_ATTESTATION_HMAC_KEY", "test-key");
        try
        {
            var forged = new AttestationEnvelope(
                "digest",
                "env:PROOF_ATTESTATION_HMAC_KEY",
                "hmac-sha256",
                Payload: "zz-not-hex");

            var result = await new HmacAttestationVerifier()
                .VerifyAsync(forged, new TrustPolicy(RequireSigned: true), CancellationToken.None);

            Assert.False(result.SignatureValid);
            Assert.False(result.Trusted);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROOF_ATTESTATION_HMAC_KEY", null);
        }
    }

    [Fact]
    public async Task Attestation_PayloadIsDomainSeparated_FromRawDigest()
    {
        Environment.SetEnvironmentVariable("PROOF_ATTESTATION_HMAC_KEY", "test-key");
        try
        {
            var envelope = await new HmacAttestationSigner().SignAsync(
                "digest", new AttestationContext("run"), CancellationToken.None);

            // 순수 다이제스트에 대한 레거시 HMAC는 검증되면 안 된다. 페이로드는
            // 도메인 태그가 붙어 있으므로 증명 서명은 절대 원시 다이제스트 MAC가 아니다.
            var legacy = AttestationHmac.ComputeHex("test-key", "digest");
            Assert.NotEqual(legacy, envelope.Payload);

            var forged = new AttestationEnvelope(
                "digest",
                "env:PROOF_ATTESTATION_HMAC_KEY",
                "hmac-sha256",
                Payload: legacy);
            var result = await new HmacAttestationVerifier()
                .VerifyAsync(forged, new TrustPolicy(RequireSigned: true), CancellationToken.None);

            Assert.False(result.SignatureValid);
            Assert.False(result.Trusted);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROOF_ATTESTATION_HMAC_KEY", null);
        }
    }

    [Fact]
    public void ReviewSignature_DoesNotAcceptAttestationSignature()
    {
        Environment.SetEnvironmentVariable("PROOF_ATTESTATION_HMAC_KEY", "test-key");
        try
        {
            var attestationSignature = AttestationHmac.ComputeHex(
                "test-key", AttestationHmac.AttestationPayload("src"));

            Assert.False(ReviewSignature.IsValid("src", "assets/logo.png", attestationSignature));
            Assert.False(ReviewSignature.IsValidV2("src", "assets/logo.png", "alice", attestationSignature));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROOF_ATTESTATION_HMAC_KEY", null);
        }
    }

    [Fact]
    public void VerifyArtifactManifest_PassesOnlyWhenFilesExistWithMatchingHash()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".distill"));
        var previous = Directory.GetCurrentDirectory();
        try
        {
            var artifact = Path.Combine(root, ".distill", "out.json");
            File.WriteAllText(artifact, "content");
            Directory.SetCurrentDirectory(root);

            var sha = CertificateCanonicalHasher.HashFile(".distill/out.json");
            var entry = new EvidenceArtifactManifestEntry("e1", ".distill/out.json", sha);

            Assert.Null(CertificateVerifyCommand.VerifyArtifactManifest([entry]));
            Assert.NotNull(CertificateVerifyCommand.VerifyArtifactManifest([
                new EvidenceArtifactManifestEntry("e2", ".distill/missing.json", sha)
            ]));
            Assert.NotNull(CertificateVerifyCommand.VerifyArtifactManifest([
                new EvidenceArtifactManifestEntry("e3", ".distill/out.json", new string('a', 64))
            ]));
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
            Directory.Delete(root, recursive: true);
        }
    }
}
