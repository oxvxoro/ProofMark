using Proof.Core;

namespace Proof.Cli;

public static class ProofConfigCompiler
{
    public static CompiledProofConfig Compile(ProofConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new CompiledProofConfig(
            ToPolicy(config),
            ToImpactSettings(config),
            new CompiledVerificationSettings(
                config.Verification.DistillProfile,
                config.Verification.DistillConfig,
                config.Verification.Cache,
                config.Verification.ImportManifest),
            new CompiledBaseRevisionSettings(config.Proof.Base.Strategy, config.Proof.Base.Ref));
    }

    public static ImpactAnalysisSettings ToImpactSettings(ProofConfig config)
        => new(
            config.Analysis.Impact.BaseDepth,
            config.Analysis.Impact.PublicDepth,
            config.Analysis.Impact.MaxResults,
            config.Analysis.Impact.CallerPageSize,
            config.Analysis.Impact.CallerMaxResults,
            config.Analysis.Impact.MinConfidence,
            config.Analysis.Impact.Profile);

    public static ProofPolicy ToPolicy(ProofConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ValidateEffects(config);
        return new ProofPolicy(
            ParseDisposition(config.Policy.Uncertainty.HeuristicImpact, UncertaintyDisposition.Advisory),
            ParseDisposition(config.Policy.Uncertainty.LocationUnknown, UncertaintyDisposition.Blocking),
            ParseDisposition(config.Policy.Uncertainty.ImpactTruncated, UncertaintyDisposition.Blocking),
            ParseDisposition(config.Policy.Uncertainty.CallerTruncated, UncertaintyDisposition.Blocking),
            PublicApiCompatibilityRequired: !string.Equals(config.Policy.PublicApiCompatibility, "advisory", StringComparison.OrdinalIgnoreCase),
            InternalConsumerCompatibilityRequired: !string.Equals(config.Policy.InternalConsumerCompatibility, "advisory", StringComparison.OrdinalIgnoreCase),
            TestMappingRequired: !string.Equals(config.Policy.TestMapping, "advisory", StringComparison.OrdinalIgnoreCase),
            StaticAnalysisRequired: string.Equals(config.Policy.StaticAnalysis, "required", StringComparison.OrdinalIgnoreCase),
            AppContractRequired: string.Equals(config.Policy.AppContract, "required", StringComparison.OrdinalIgnoreCase),
            Architecture: ParseArchitectureMode(config.Policy.Architecture),
            MinConfidence: config.Analysis.Impact.MinConfidence,
            TestMaps: [.. config.Policy.TestMaps.Select(entry => new TestMapEntry(entry.Symbol, entry.Tests))],
            PathRules: [.. config.Policy.Paths.Select(rule => new PathRule(rule.Match, rule.Effect))],
            FailOnUncertainCodes: [.. config.Policy.Ci.FailOnUncertainCodes],
            TestMappingProjects: config.Policy.TestMappingProjects is { Count: > 0 } ? [.. config.Policy.TestMappingProjects] : null,
            ManualReviewAcceptedSchemas: config.Policy.ManualReview.AcceptedSchemas is { Count: > 0 }
                ? [.. config.Policy.ManualReview.AcceptedSchemas]
                : null);
    }

    private static void ValidateEffects(ProofConfig config)
    {
        foreach (var rule in config.Policy.Paths)
        {
            if (string.Equals(rule.Effect, "ignore", StringComparison.OrdinalIgnoreCase)
                || string.Equals(rule.Effect, "manual-review", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            throw new ProofConfigException(
                $"Unsupported policy.paths effect '{rule.Effect}'. Allowed effects: 'ignore', 'manual-review'.");
        }
    }

    private static UncertaintyDisposition ParseDisposition(string value, UncertaintyDisposition fallback)
        => string.Equals(value, "advisory", StringComparison.OrdinalIgnoreCase)
            ? UncertaintyDisposition.Advisory
            : string.Equals(value, "blocking", StringComparison.OrdinalIgnoreCase)
                ? UncertaintyDisposition.Blocking
                : fallback;

    private static ArchitecturePolicyMode ParseArchitectureMode(string value)
        => value.ToLowerInvariant() switch
        {
            "advisory" => ArchitecturePolicyMode.Advisory,
            "required" => ArchitecturePolicyMode.Required,
            _ => ArchitecturePolicyMode.Off
        };
}
