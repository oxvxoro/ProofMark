using Proof.Core;

namespace Proof.Engine;

internal static class RunIdFactory
{
    internal static string Create(ChangeImpact impact, ProofPlan plan, VerificationPlan? verificationPlan)
    {
        var sourceDigest = impact.SourceDigest ?? plan.SourceDigest;
        var scopeKey = string.Join(
            '|',
            impact.BaseRevision ?? string.Empty,
            impact.HeadRevision ?? string.Empty,
            verificationPlan?.Profile ?? string.Empty);
        if (string.IsNullOrWhiteSpace(sourceDigest))
        {
            return $"proof-nosrc-{CertificateCanonicalHasher.HashText(scopeKey)[..8]}";
        }

        var digest = sourceDigest.ToLowerInvariant();
        return $"proof-{digest[..Math.Min(12, digest.Length)]}-{CertificateCanonicalHasher.HashText(scopeKey)[..8]}";
    }
}
