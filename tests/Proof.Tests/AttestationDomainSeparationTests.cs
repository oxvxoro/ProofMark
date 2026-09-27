using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

/// <summary>
/// PR-15 증명 도메인 분리: 새 봉투는 v2 태그를 쓰고, 레거시
/// v1 봉투는 마이그레이션 중에도 검증되며, 어느 태그도
/// manual-review 페이로드와 혼동될 수 없다.
/// </summary>
[Collection("AttestationEnvironment")]
public sealed class AttestationDomainSeparationTests
{
    private const string Key = "attestation-domain-test-key";

    [Fact]
    public async Task CurrentSigner_UsesV2Payload_AndVerifies()
    {
        Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, Key);
        try
        {
            Assert.NotEqual(
                AttestationHmac.AttestationPayload("digest"),
                AttestationHmac.AttestationPayloadV1("digest"));

            var signed = await new HmacAttestationSigner()
                .SignAsync("digest", new AttestationContext("run"), CancellationToken.None);

            Assert.Equal(
                AttestationHmac.ComputeHex(Key, AttestationHmac.AttestationPayload("digest")),
                signed.Payload);

            var result = await new HmacAttestationVerifier()
                .VerifyAsync(signed, new TrustPolicy(RequireSigned: true), CancellationToken.None);
            Assert.True(result.SignatureValid);
            Assert.True(result.Trusted);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, null);
        }
    }

    [Fact]
    public async Task LegacyV1Envelope_StillVerifies()
    {
        Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, Key);
        try
        {
            var payload = AttestationHmac.ComputeHex(Key, AttestationHmac.AttestationPayloadV1("digest"));
            var envelope = new AttestationEnvelope(
                "digest",
                "env:PROOF_ATTESTATION_HMAC_KEY",
                "hmac-sha256",
                payload);

            var result = await new HmacAttestationVerifier()
                .VerifyAsync(envelope, new TrustPolicy(RequireSigned: true), CancellationToken.None);

            Assert.True(result.SignatureValid);
            Assert.True(result.Trusted);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, null);
        }
    }

    [Fact]
    public async Task ReviewPayload_DoesNotVerifyAsAttestation()
    {
        Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, Key);
        try
        {
            var reviewPayload = AttestationHmac.ComputeHex(
                Key,
                AttestationHmac.ReviewPayloadV2("digest", "subject", "reviewer"));
            var envelope = new AttestationEnvelope(
                "digest",
                "env:PROOF_ATTESTATION_HMAC_KEY",
                "hmac-sha256",
                reviewPayload);

            var result = await new HmacAttestationVerifier()
                .VerifyAsync(envelope, new TrustPolicy(RequireSigned: true), CancellationToken.None);

            Assert.False(result.SignatureValid);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, null);
        }
    }
}
