using Proof.Core;

namespace Proof.Engine;

/// <summary>
/// 증명 서명자. 환경에서 공유 HMAC 키를 읽는다. 키가 있으면
/// <c>hmac-sha256</c> 봉투를 내고, 없으면(fork PR, 로컬 실행) 서명 없는
/// (<c>none</c>) 봉투를 낸다. 키 자체는 봉투나 인증서에
/// 결코 나타나지 않는다.
/// </summary>
public sealed class HmacAttestationSigner : IAttestationSigner
{
    public const string HmacKeyEnvironmentVariable = AttestationHmac.KeyEnvironmentVariable;

    public Task<AttestationEnvelope> SignAsync(
        string statementDigest,
        AttestationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var signature = ComputeHmacHex(statementDigest);
        if (signature is not null)
        {
            // 최소한의 실제 서명. statement 다이제스트에 대한 HMAC-SHA256.
            return Task.FromResult(new AttestationEnvelope(
                statementDigest,
                SignerIdentity: "env:PROOF_ATTESTATION_HMAC_KEY",
                Provider: AttestationProviders.HmacSha256,
                Payload: signature,
                Claims: BuildClaims(context)));
        }

        return Task.FromResult(new AttestationEnvelope(
            statementDigest,
            SignerIdentity: "unsigned",
            Provider: AttestationProviders.None,
            Claims: BuildClaims(context)));
    }

    private static IReadOnlyDictionary<string, string> BuildClaims(AttestationContext context)
    {
        var claims = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["runId"] = context.RunId
        };
        AddClaim(claims, "repository", context.Repository);
        AddClaim(claims, "branch", context.Branch);
        AddClaim(claims, "ref", context.Ref);
        AddClaim(claims, "workflowRef", context.WorkflowRef);
        AddClaim(claims, "workflowSha", context.WorkflowSha);
        AddClaim(claims, "commitSha", context.CommitSha);
        AddClaim(claims, "runAttempt", context.RunAttempt);
        return claims;
    }

    private static void AddClaim(IDictionary<string, string> claims, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            claims[key] = value;
        }
    }

    internal static string? ComputeHmacHex(string statementDigest)
        => AttestationHmac.ComputeHex(AttestationHmac.AttestationPayload(statementDigest));

    // v2 도메인 태그 이전에 발급된 봉투를 검증하기 위한 레거시 v1 페이로드.
    internal static string? ComputeHmacHexV1(string statementDigest)
        => AttestationHmac.ComputeHex(AttestationHmac.AttestationPayloadV1(statementDigest));
}

public sealed class HmacAttestationVerifier : IAttestationVerifier
{
    public Task<AttestationVerificationResult> VerifyAsync(
        AttestationEnvelope envelope,
        TrustPolicy policy,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var unsigned = string.Equals(envelope.Provider, AttestationProviders.None, StringComparison.OrdinalIgnoreCase);
        var hmac = string.Equals(envelope.Provider, AttestationProviders.HmacSha256, StringComparison.OrdinalIgnoreCase);

        if (!unsigned && !hmac)
        {
            return Task.FromResult(new AttestationVerificationResult(false, false, "Unknown attestation provider."));
        }

        if (hmac)
        {
            var expected = HmacAttestationSigner.ComputeHmacHex(envelope.StatementDigest);
            var legacy = HmacAttestationSigner.ComputeHmacHexV1(envelope.StatementDigest);
            if (expected is null || legacy is null)
            {
                return Task.FromResult(new AttestationVerificationResult(
                    false, false, "HMAC key is not available to verify the attestation."));
            }

            // 현재 v2 도메인 태그를 받아들이고, 마이그레이션 중에는 레거시
            // v1 태그도 받아들인다. 두 경우 모두 리뷰/증명 도메인 분리는
            // 유지된다. 페이로드 접두사가 여전히 다르기 때문이다.
            var matches = !string.IsNullOrWhiteSpace(envelope.Payload)
                && (CryptographicEquals(expected, envelope.Payload)
                    || CryptographicEquals(legacy, envelope.Payload));
            if (!matches)
            {
                return Task.FromResult(new AttestationVerificationResult(
                    false, false, "HMAC attestation payload does not match the statement digest."));
            }
        }

        // 유효한 서명(또는 서명 없는 봉투)만으로는 부족하다. 허용 목록이
        // 설정되면 서명자 신원이 신뢰되어야 한다.
        if (!IsIssuerTrusted(envelope.SignerIdentity, policy.TrustedIssuers))
        {
            return Task.FromResult(new AttestationVerificationResult(
                true, false, "Attestation signer identity is not in the trusted issuer list."));
        }

        if (unsigned && policy.RequireSigned)
        {
            return Task.FromResult(new AttestationVerificationResult(
                true, false, "RequireSigned is true but the attestation is not HMAC-signed."));
        }

        // 신원 클레임은 신뢰 검사다. 서명 검사가 결코 아니다. 유효한
        // 서명이라도 repository/ref/workflow/commit이 어긋나면 신뢰되지 않는다.
        var identityFailure = CheckIdentityClaims(envelope, policy);
        if (identityFailure is not null)
        {
            return Task.FromResult(new AttestationVerificationResult(true, false, identityFailure));
        }

        return Task.FromResult(new AttestationVerificationResult(true, true, null));
    }

    private static string? CheckIdentityClaims(AttestationEnvelope envelope, TrustPolicy policy)
    {
        var claims = envelope.Claims;

        if (!string.IsNullOrWhiteSpace(policy.Repository)
            && !ClaimEquals(claims, "repository", policy.Repository))
        {
            return $"Attestation repository claim does not match the trusted repository '{policy.Repository}'.";
        }

        if (policy.AllowedRefs is { Count: > 0 } allowedRefs)
        {
            if (!TryGetClaim(claims, "ref", out var reference)
                && !TryGetClaim(claims, "branch", out reference))
            {
                return "Attestation has no ref/branch claim to match against the allowed refs.";
            }

            if (!allowedRefs.Any(item => string.Equals(item, reference, StringComparison.Ordinal)))
            {
                return $"Attestation ref '{reference}' is not in the allowed refs.";
            }
        }

        if (policy.AllowedWorkflows is { Count: > 0 } allowedWorkflows)
        {
            if (!TryGetClaim(claims, "workflowRef", out var workflowRef))
            {
                return "Attestation has no workflowRef claim to match against the allowed workflows.";
            }

            if (!allowedWorkflows.Any(item => string.Equals(item, workflowRef, StringComparison.Ordinal)))
            {
                return $"Attestation workflow '{workflowRef}' is not in the allowed workflows.";
            }
        }

        if (!string.IsNullOrWhiteSpace(policy.ExpectedCommitSha)
            && !ClaimEquals(claims, "commitSha", policy.ExpectedCommitSha))
        {
            return $"Attestation commit claim does not match the expected commit '{policy.ExpectedCommitSha}'.";
        }

        return null;
    }

    private static bool ClaimEquals(IReadOnlyDictionary<string, string>? claims, string key, string expected)
        => TryGetClaim(claims, key, out var value)
           && string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

    private static bool TryGetClaim(IReadOnlyDictionary<string, string>? claims, string key, out string value)
    {
        if (claims is not null && claims.TryGetValue(key, out var found) && !string.IsNullOrWhiteSpace(found))
        {
            value = found;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool IsIssuerTrusted(string? signerIdentity, IReadOnlyList<string>? trustedIssuers)
    {
        if (trustedIssuers is not { Count: > 0 })
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(signerIdentity)
            && trustedIssuers.Any(issuer => string.Equals(issuer, signerIdentity, StringComparison.OrdinalIgnoreCase));
    }

    // 디코드된 HMAC 바이트의 상수 시간 비교. 잘못되었거나
    // 길이가 다른 페이로드는 평범한 불일치다. 예외를 결코 던지지 않는다.
    private static bool CryptographicEquals(string expected, string actual)
    {
        byte[] expectedBytes;
        byte[] actualBytes;
        try
        {
            expectedBytes = Convert.FromHexString(expected);
            actualBytes = Convert.FromHexString(actual);
        }
        catch (FormatException)
        {
            return false;
        }

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
