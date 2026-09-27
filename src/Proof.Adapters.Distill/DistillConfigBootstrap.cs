using Distill.Core.Config;

namespace Proof.Adapters.Distill;

/// <summary>
/// distill.yml이 없으면 만든다(proof init이 Distill.Cli를 참조하지 않고 사용한다).
/// </summary>
public static class DistillConfigBootstrap
{
    public static bool TryCreateDefaultConfig(string workspaceRoot, out string? configPath, out string? message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        configPath = DefaultDistillWorkspaceDiscovery.ResolveConfigPath(workspaceRoot);
        if (File.Exists(configPath))
        {
            message = null;
            return false;
        }

        var config = DefaultDistillWorkspaceDiscovery.CreateDefaultConfig(workspaceRoot);
        var yaml = DefaultDistillWorkspaceDiscovery.SerializeConfig(config);
        var directory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(configPath, yaml);
        message = $"Created {configPath}.";
        return true;
    }
}
