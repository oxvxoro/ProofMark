using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

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
