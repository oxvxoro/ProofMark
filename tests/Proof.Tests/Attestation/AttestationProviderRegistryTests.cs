using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

public sealed class AttestationProviderRegistryTests
{
    [Theory]
    [InlineData("none")]
    [InlineData("hmac-sha256")]
    [InlineData("HMAC-SHA256")]
    public void Resolve_returns_the_built_in_verifier_for_supported_providers(string provider)
        => Assert.IsType<HmacAttestationVerifier>(AttestationProviderRegistry.Resolve(provider));

    [Theory]
    [InlineData("sigstore")]
    [InlineData("dsse")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Resolve_returns_an_untrusted_verifier_for_unknown_providers(string? provider)
    {
        var verifier = AttestationProviderRegistry.Resolve(provider);

        var result = await verifier.VerifyAsync(
            new AttestationEnvelope("digest", "signer", provider ?? "none", "payload"),
            new TrustPolicy(RequireSigned: true),
            CancellationToken.None);

        Assert.False(result.SignatureValid);
        Assert.False(result.Trusted);
        Assert.Contains("Unsupported", result.Reason, StringComparison.OrdinalIgnoreCase);
    }
}
