using Proof.Core;

namespace Proof.Engine;

/// <summary>
/// 증명 제공자 id를 검증기에 대응한다. 지금은 내장
/// <c>none</c> / <c>hmac-sha256</c> 제공자만 등록된다. 이것은
/// 이후의 비대칭 또는 키 없는 제공자를 위한 확장 경계다. 새
/// <see cref="IAttestationVerifier"/>를 여기에 등록해도 CLI나
/// 인증서 스키마는 건드리지 않는다.
/// </summary>
public static class AttestationProviderRegistry
{
    private static readonly IAttestationVerifier HmacVerifier = new HmacAttestationVerifier();

    public static IAttestationVerifier Resolve(string? provider)
        => AttestationProviders.IsSupported(provider)
            ? HmacVerifier
            : new UnsupportedAttestationVerifier(provider);

    private sealed class UnsupportedAttestationVerifier : IAttestationVerifier
    {
        private readonly string? _provider;

        internal UnsupportedAttestationVerifier(string? provider) => _provider = provider;

        public Task<AttestationVerificationResult> VerifyAsync(
            AttestationEnvelope envelope,
            TrustPolicy policy,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 알 수 없는 제공자는 서명된 것처럼 보여도 결코 신뢰되지 않는다.
            // 등록된 검증기가 없으면 서명을 검사할 수 없다.
            return Task.FromResult(new AttestationVerificationResult(
                SignatureValid: false,
                Trusted: false,
                Reason: $"Unsupported attestation provider '{_provider}'; no verifier is registered for it."));
        }
    }
}
