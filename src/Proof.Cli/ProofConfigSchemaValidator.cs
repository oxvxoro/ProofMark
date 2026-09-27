using Proof.Core;
using YamlDotNet.Serialization;

namespace Proof.Cli;

public static class ProofConfigSchemaValidator
{
    public static void Validate(ProofConfig config, string? workspaceRoot = null, bool requireExistingAnalysisSolution = true)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.Version is not (1 or 2))
        {
            throw new ProofConfigException($"Unsupported proof.yml version: {config.Version}");
        }

        if (config.Proof.Base.Strategy is not ("mergeBase" or "direct" or "explicitCommit"))
        {
            throw new ProofConfigException($"Unsupported proof.base.strategy '{config.Proof.Base.Strategy}'.");
        }

        if (string.IsNullOrWhiteSpace(config.Verification.DistillProfile))
        {
            throw new ProofConfigException("verification.distillProfile is required.");
        }

        if (config.Analysis.Impact.Profile is not ("code" or "app"))
        {
            throw new ProofConfigException(
                $"Unsupported analysis.impact.profile '{config.Analysis.Impact.Profile}'. Allowed values: 'code', 'app'.");
        }

        if (config.Policy.AppContract is not ("off" or "required"))
        {
            throw new ProofConfigException(
                $"Unsupported policy.appContract '{config.Policy.AppContract}'. Allowed values: 'off', 'required'.");
        }

        if (config.Policy.Architecture is not ("off" or "advisory" or "required"))
        {
            throw new ProofConfigException(
                $"Unsupported policy.architecture '{config.Policy.Architecture}'. Allowed values: 'off', 'advisory', 'required'.");
        }

        if (config.Policy.TestMappingProjects is not null && config.Policy.TestMappingProjects.Any(entry => string.IsNullOrWhiteSpace(entry)))
        {
            throw new ProofConfigException(
                "policy.testMappingProjects entries must be non-empty project names.");
        }

        if (!string.IsNullOrWhiteSpace(config.Analysis.Solution))
        {
            AnalysisTargetResolver.ValidateConfiguredPath(config.Analysis.Solution);
            if (workspaceRoot is not null && requireExistingAnalysisSolution)
            {
                AnalysisTargetResolver.Resolve(workspaceRoot, config.Analysis.Solution);
            }
        }
    }

    public static void RejectUnknownProperties(string yaml, IDeserializer deserializer)
    {
        var raw = deserializer.Deserialize<Dictionary<string, object>>(yaml);
        if (raw is null)
        {
            return;
        }

        RejectKeys(raw, ["version", "proof", "verification", "policy", "analysis"], "proof.yml");
        if (TryMap(raw, "proof", out var proof))
        {
            RejectKeys(proof, ["base"], "proof");
            if (TryMap(proof, "base", out var baseSection))
            {
                RejectKeys(baseSection, ["strategy", "ref"], "proof.base");
            }
        }

        if (TryMap(raw, "verification", out var verification))
        {
            RejectKeys(verification, ["distillProfile", "distillConfig", "cache", "importManifest"], "verification");
        }

        if (TryMap(raw, "policy", out var policy))
        {
            RejectKeys(policy, ["publicApiCompatibility", "internalConsumerCompatibility", "testMapping", "testMappingProjects", "staticAnalysis", "appContract", "architecture", "testMaps", "paths", "ci", "uncertainty", "manualReview"], "policy");
            if (TryMap(policy, "ci", out var ci))
            {
                RejectKeys(ci, ["failOnUncertainCodes"], "policy.ci");
            }

            if (TryMap(policy, "uncertainty", out var uncertainty))
            {
                RejectKeys(uncertainty, ["heuristicImpact", "locationUnknown", "impactTruncated", "callerTruncated"], "policy.uncertainty");
            }

            if (TryMap(policy, "manualReview", out var manualReview))
            {
                RejectKeys(manualReview, ["acceptedSchemas"], "policy.manualReview");
            }
        }

        if (TryMap(raw, "analysis", out var analysis))
        {
            RejectKeys(analysis, ["solution", "impact", "indexBaseRevision"], "analysis");
            if (TryMap(analysis, "impact", out var impact))
            {
                RejectKeys(impact, ["profile", "baseDepth", "publicDepth", "maxResults", "callerPageSize", "callerMaxResults", "minConfidence"], "analysis.impact");
            }
        }
    }

    private static bool TryMap(Dictionary<string, object> node, string key, out Dictionary<string, object> map)
    {
        map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (!node.TryGetValue(key, out var value) && !node.Keys.Any(item => string.Equals(item, key, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var match = node.First(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
        if (match is Dictionary<object, object> objectMap)
        {
            foreach (var pair in objectMap)
            {
                map[pair.Key.ToString() ?? string.Empty] = pair.Value;
            }

            return true;
        }

        if (match is Dictionary<string, object> stringMap)
        {
            map = stringMap;
            return true;
        }

        return false;
    }

    private static void RejectKeys(Dictionary<string, object> node, IReadOnlyList<string> allowed, string path)
    {
        foreach (var key in node.Keys)
        {
            if (!allowed.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                throw new ProofConfigException($"Unknown configuration property '{path}.{key}'.");
            }
        }
    }
}
