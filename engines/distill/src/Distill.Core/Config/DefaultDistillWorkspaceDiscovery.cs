namespace Distill.Core.Config;

/// <summary>
/// 기본 distill.yml 생성을 위한 워크스페이스 레이아웃 탐색(distill init과 proof init이 공유한다).
/// </summary>
public static class DefaultDistillWorkspaceDiscovery
{
    public static string? FindSolution(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var slnx = Directory.EnumerateFiles(workspaceRoot, "*.slnx").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if (slnx is not null)
        {
            return slnx;
        }

        return Directory.EnumerateFiles(workspaceRoot, "*.sln").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    public static IReadOnlyList<string> FindTestProjects(string workspaceRoot)
        => Directory
            .EnumerateFiles(workspaceRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(project =>
                project.Contains("Tests", StringComparison.OrdinalIgnoreCase)
                || project.Contains(".Test.", StringComparison.OrdinalIgnoreCase)
                || project.EndsWith("Tests.csproj", StringComparison.OrdinalIgnoreCase))
            .OrderBy(project => project, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static string ResolveConfigPath(string workspaceRoot)
    {
        var direct = Path.Combine(workspaceRoot, "distill.yml");
        if (File.Exists(direct))
        {
            return direct;
        }

        var dotDistill = Path.Combine(workspaceRoot, ".distill", "distill.yml");
        if (File.Exists(dotDistill))
        {
            return dotDistill;
        }

        return direct;
    }

    public static DistillConfig CreateDefaultConfig(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var solution = FindSolution(workspaceRoot);
        var testProjects = FindTestProjects(workspaceRoot);
        var unitProject = testProjects.FirstOrDefault();
        var buildTarget = solution is not null
            ? Path.GetFileName(solution)
            : unitProject is not null
                ? MakeRelativePath(workspaceRoot, unitProject)
                : FindFirstProject(workspaceRoot) is { } project
                    ? MakeRelativePath(workspaceRoot, project)
                    : ".";

        var config = new DistillConfig
        {
            Profiles =
            {
                ["quick"] = new ProfileDefinition { Checks = ["build", "unit"] },
                ["full"] = new ProfileDefinition { Checks = ["build", "unit", "format"] }
            },
            Checks =
            {
                ["build"] = new CheckConfig
                {
                    Kind = "build",
                    Command = $"dotnet build {buildTarget}",
                    Source = "msbuild-binlog",
                    Timeout = 300,
                    StopOnFailure = true
                },
                ["unit"] = new CheckConfig
                {
                    Kind = "test",
                    Command = unitProject is not null
                        ? $"dotnet test {MakeRelativePath(workspaceRoot, unitProject)} --no-build"
                        : solution is not null
                            ? $"dotnet test {Path.GetFileName(solution)} --no-build"
                            : "dotnet test --no-build",
                    Source = "auto",
                    DependsOn = ["build"],
                    Timeout = 600,
                    StopOnFailure = true
                },
                ["format"] = new CheckConfig
                {
                    Kind = "format",
                    Command = solution is not null
                        ? $"dotnet format {Path.GetFileName(solution)} --verify-no-changes"
                        : "dotnet format --verify-no-changes",
                    Source = "generic",
                    DependsOn = ["build"],
                    Timeout = 300,
                    StopOnFailure = true
                }
            }
        };

        if (solution is not null)
        {
            config.Workspace.Solution = Path.GetFileName(solution);
        }

        return config;
    }

    public static string SerializeConfig(DistillConfig config)
        => DistillConfigLoader.Serialize(config);

    private static string? FindFirstProject(string workspaceRoot)
        => Directory
            .EnumerateFiles(workspaceRoot, "*.csproj", SearchOption.AllDirectories)
            .OrderBy(project => project, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    private static string MakeRelativePath(string workspaceRoot, string absolutePath)
    {
        var relative = Path.GetRelativePath(workspaceRoot, absolutePath);
        return relative.Replace('\\', '/');
    }
}
