using Proof.Core;

namespace Proof.Engine;

public sealed class ProofEvidenceCapabilitySource : IEvidenceCapabilitySource
{
    public IReadOnlyList<EvidenceCapability> GetCapabilities(CapabilityContext context)
    {
        _ = context;
        return ProofProducerCapabilities.Create();
    }
}

public sealed class VerificationCapabilityCatalog(IReadOnlyList<IEvidenceCapabilitySource> sources)
{
    public IReadOnlyList<EvidenceCapability> Build(CapabilityContext context)
    {
        var merged = new List<EvidenceCapability>();
        foreach (var source in sources)
        {
            foreach (var capability in source.GetCapabilities(context))
            {
                var existing = merged.FindIndex(item =>
                    string.Equals(item.CheckId, capability.CheckId, StringComparison.OrdinalIgnoreCase));
                if (existing >= 0)
                {
                    if (!SameDescriptor(merged[existing], capability))
                    {
                        throw new InvalidOperationException(
                            $"Capability '{capability.CheckId}' is declared with conflicting descriptors.");
                    }

                    continue;
                }

                merged.Add(capability);
            }
        }

        return merged;
    }

    private static bool SameDescriptor(EvidenceCapability left, EvidenceCapability right)
        => string.Equals(left.Kind, right.Kind, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.CommandTarget, right.CommandTarget, StringComparison.OrdinalIgnoreCase)
            && left.ScopeMode == right.ScopeMode;
}
