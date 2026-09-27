using Distill.Core.Planning;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Distill.Core.Config;

public static class DistillConfigLoader
{
    private const string DefaultRunDir = ".distill/runs";

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static DistillConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Config file not found: {path}", path);
        }

        var yaml = File.ReadAllText(path);
        return LoadFromYaml(yaml);
    }

    public static DistillConfig LoadFromYaml(string yaml)
    {
        DistillConfig config;
        try
        {
            config = Deserializer.Deserialize<DistillConfig>(yaml)
                     ?? throw new InvalidOperationException("Config deserialized to null.");
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new InvalidOperationException($"Invalid distill.yml: {ex.Message}", ex);
        }

        Validate(config);
        return config;
    }

    public static void Validate(DistillConfig config)
    {
        if (config.Version != 1)
        {
            throw new InvalidOperationException($"Unsupported config version: {config.Version}");
        }

        if (config.Reliability.MinConfidence is < 0 or > 1)
        {
            throw new InvalidOperationException("reliability.minConfidence must be between 0 and 1.");
        }

        if (config.Testing.Platform is not ("auto" or "vstest" or "mtp"))
        {
            throw new InvalidOperationException(
                $"Unsupported testing.platform '{config.Testing.Platform}'. Expected auto, vstest, or mtp.");
        }

        foreach (var pattern in config.Redaction.Patterns)
        {
            try
            {
                _ = new Regex(pattern);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException(
                    $"Invalid redaction pattern '{pattern}': {ex.Message}",
                    ex);
            }
        }

        foreach (var (profileName, profile) in config.Profiles)
        {
            if (profile.Checks.Count == 0)
            {
                throw new InvalidOperationException($"Profile '{profileName}' must include at least one check.");
            }

            foreach (var checkId in profile.Checks)
            {
                if (!config.Checks.ContainsKey(checkId))
                {
                    throw new InvalidOperationException($"Profile '{profileName}' references unknown check '{checkId}'.");
                }
            }
        }

        if (!string.Equals(config.Workspace.RunDir, DefaultRunDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"workspace.runDir '{config.Workspace.RunDir}' is not supported. Only the default '{DefaultRunDir}' is currently supported.");
        }

        foreach (var (checkId, check) in config.Checks)
        {
            if (check.Timeout <= 0)
            {
                throw new InvalidOperationException($"Check '{checkId}' timeout must be positive.");
            }

            foreach (var dependency in check.DependsOn)
            {
                if (!config.Checks.ContainsKey(dependency))
                {
                    throw new InvalidOperationException($"Check '{checkId}' depends on unknown check '{dependency}'.");
                }
            }

            ValidateCheckCommand(checkId, check);
        }

        ValidateDependencyGraphIsAcyclic(config);
    }

    private static void ValidateCheckCommand(string checkId, CheckConfig check)
    {
        var kind = check.Kind.ToLowerInvariant();
        if ((kind is "apicompatibility" or "api_compatibility" or "api-compatibility")
            && string.IsNullOrWhiteSpace(check.Command))
        {
            return;
        }

        if (string.Equals(kind, "process", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(check.Command))
            {
                throw new InvalidOperationException($"Check '{checkId}' is kind process but command is empty.");
            }

            if (string.Equals(check.Source, "junit", StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(check.Artifact) || string.IsNullOrWhiteSpace(check.Project)))
            {
                throw new InvalidOperationException(
                    $"Check '{checkId}' uses source junit but artifact or project is empty.");
            }

            if (!string.Equals(check.Source, "junit", StringComparison.OrdinalIgnoreCase)
                && (!string.IsNullOrWhiteSpace(check.Artifact) || !string.IsNullOrWhiteSpace(check.Project)))
            {
                throw new InvalidOperationException(
                    $"Check '{checkId}' sets artifact or project without source junit.");
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(check.Artifact)
            || !string.IsNullOrWhiteSpace(check.Project)
            || !string.IsNullOrWhiteSpace(check.Coverage)
            || string.Equals(check.Source, "junit", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Check '{checkId}' sets artifact, project, coverage, or source junit but is not kind process.");
        }

        DotnetCommandParser.ParsedDotnetCommand parsed;
        try
        {
            parsed = DotnetCommandParser.Parse(check.Command);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException($"Check '{checkId}' has invalid command: {ex.Message}", ex);
        }

        if (string.Equals(kind, "build", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(parsed.Verb, "build", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Check '{checkId}' is kind build but command verb is '{parsed.Verb}'.");
        }

        if (string.Equals(kind, "test", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(parsed.Verb, "test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Check '{checkId}' is kind test but command verb is '{parsed.Verb}'.");
        }
    }

    private static void ValidateDependencyGraphIsAcyclic(DistillConfig config)
    {
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visit(string checkId)
        {
            if (visited.Contains(checkId))
            {
                return;
            }

            if (!visiting.Add(checkId))
            {
                throw new InvalidOperationException($"Dependency cycle detected involving check '{checkId}'.");
            }

            if (config.Checks.TryGetValue(checkId, out var check))
            {
                foreach (var dependency in check.DependsOn)
                {
                    Visit(dependency);
                }
            }

            visiting.Remove(checkId);
            visited.Add(checkId);
        }

        foreach (var checkId in config.Checks.Keys)
        {
            Visit(checkId);
        }
    }

    public static string Serialize(DistillConfig config)
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        return serializer.Serialize(config);
    }
}
