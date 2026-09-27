namespace Proof.Core;

public static class EvidenceCheckIds
{
    public const string ApiCompatibility = "api-compatibility";
    public const string TestMapping = "test-mapping";
    public const string RuntimeCoverage = "runtime-coverage";
    public const string ManualReview = "manual-review";
    public const string Architecture = "architecture";
}

public sealed record CapabilityContext(string WorkspaceRoot, string? Profile);

public interface IEvidenceCapabilitySource
{
    IReadOnlyList<EvidenceCapability> GetCapabilities(CapabilityContext context);
}

public static class ProofProducerCapabilities
{
    public static IReadOnlyList<EvidenceCapability> Create()
        =>
        [
            new EvidenceCapability(EvidenceCheckIds.ApiCompatibility, "apicompatibility", null, ScopeMode.RepositoryWide),
            new EvidenceCapability(EvidenceCheckIds.TestMapping, "testmapping", null, ScopeMode.Exact),
            new EvidenceCapability(EvidenceCheckIds.RuntimeCoverage, "runtimecoverage", null, ScopeMode.Exact),
            new EvidenceCapability(EvidenceCheckIds.ManualReview, "manualreview", null, ScopeMode.Exact),
            new EvidenceCapability(EvidenceCheckIds.Architecture, "architecture", null, ScopeMode.Exact)
        ];

    public static IReadOnlyList<EvidenceCapability> Merge(
        IReadOnlyList<EvidenceCapability> catalog,
        string? profile = null)
    {
        _ = profile;
        var merged = catalog.ToList();
        foreach (var producer in Create())
        {
            if (Occupied(merged, producer))
            {
                continue;
            }

            merged.Add(producer);
        }

        return merged;
    }

    private static bool Occupied(List<EvidenceCapability> merged, EvidenceCapability producer)
    {
        if (producer.CheckId == EvidenceCheckIds.ApiCompatibility)
        {
            return merged.Any(item =>
                item.Kind.Contains("api", StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.CheckId, EvidenceCheckIds.ApiCompatibility, StringComparison.OrdinalIgnoreCase));
        }

        if (producer.CheckId == EvidenceCheckIds.TestMapping)
        {
            return merged.Any(item =>
                item.Kind.Contains("testmapping", StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.CheckId, EvidenceCheckIds.TestMapping, StringComparison.OrdinalIgnoreCase));
        }

        if (producer.CheckId == EvidenceCheckIds.RuntimeCoverage)
        {
            return merged.Any(item =>
                item.Kind.Contains("runtimecoverage", StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.CheckId, EvidenceCheckIds.RuntimeCoverage, StringComparison.OrdinalIgnoreCase));
        }

        if (producer.CheckId == EvidenceCheckIds.ManualReview)
        {
            return merged.Any(item =>
                item.Kind.Contains("manualreview", StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.CheckId, EvidenceCheckIds.ManualReview, StringComparison.OrdinalIgnoreCase));
        }

        if (producer.CheckId == EvidenceCheckIds.Architecture)
        {
            return merged.Any(item =>
                item.Kind.Contains("architecture", StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.CheckId, EvidenceCheckIds.Architecture, StringComparison.OrdinalIgnoreCase));
        }

        return merged.Any(item => string.Equals(item.CheckId, producer.CheckId, StringComparison.OrdinalIgnoreCase));
    }
}
