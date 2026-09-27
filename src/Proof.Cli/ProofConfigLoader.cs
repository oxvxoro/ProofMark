using Proof.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Proof.Cli;

public static class ProofConfigLoader
{
    public static ProofConfig Load(string workspaceRoot)
    {
        var path = ResolveConfigPath(workspaceRoot);
        if (!File.Exists(path))
        {
            return new ProofConfig();
        }

        return LoadFromPath(path, workspaceRoot, requireExistingAnalysisSolution: true);
    }

    internal static ProofConfig LoadForBootstrap(string workspaceRoot)
    {
        var path = ResolveConfigPath(workspaceRoot);
        if (!File.Exists(path))
        {
            return new ProofConfig();
        }

        return LoadFromPath(path, workspaceRoot, requireExistingAnalysisSolution: false);
    }

    public static ProofConfig LoadedFromFile(string path, string? workspaceRoot = null)
        => LoadFromPath(path, workspaceRoot, requireExistingAnalysisSolution: true);

    public static string ResolveConfigPath(string workspaceRoot)
    {
        var direct = Path.Combine(workspaceRoot, "proof.yml");
        if (File.Exists(direct))
        {
            return direct;
        }

        return Path.Combine(workspaceRoot, ".proof", "proof.yml");
    }

    private static ProofConfig LoadFromPath(string path, string? workspaceRoot, bool requireExistingAnalysisSolution)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        try
        {
            var yaml = File.ReadAllText(path);
            ProofConfigSchemaValidator.RejectUnknownProperties(yaml, deserializer);
            var config = deserializer.Deserialize<ProofConfig>(yaml) ?? new ProofConfig();
            config.Validate(workspaceRoot, requireExistingAnalysisSolution);
            return config;
        }
        catch (YamlDotNet.Core.YamlException exception)
        {
            throw new ProofConfigException($"Invalid proof.yml: {exception.Message}", exception);
        }
    }
}
