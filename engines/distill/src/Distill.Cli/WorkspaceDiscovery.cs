using Distill.Core.Config;

namespace Distill.Cli;

public static class WorkspaceDiscovery
{
    public static string? FindSolution(string workspaceRoot)
        => DefaultDistillWorkspaceDiscovery.FindSolution(workspaceRoot);

    public static IReadOnlyList<string> FindTestProjects(string workspaceRoot)
        => DefaultDistillWorkspaceDiscovery.FindTestProjects(workspaceRoot);

    public static string ResolveConfigPath(string workspaceRoot)
        => DefaultDistillWorkspaceDiscovery.ResolveConfigPath(workspaceRoot);

    public static DistillConfig CreateDefaultConfig(string workspaceRoot)
        => DefaultDistillWorkspaceDiscovery.CreateDefaultConfig(workspaceRoot);

    public static string SerializeConfig(DistillConfig config)
        => DefaultDistillWorkspaceDiscovery.SerializeConfig(config);
}
