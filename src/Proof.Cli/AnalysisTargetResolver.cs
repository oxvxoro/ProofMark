using Proof.Core;

namespace Proof.Cli;

internal static class AnalysisTargetResolver
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".sln",
        ".slnx",
        ".csproj"
    };

    public static string? Resolve(string workspaceRoot, string? solution)
    {
        if (string.IsNullOrWhiteSpace(solution))
        {
            return null;
        }

        ValidateConfiguredPath(solution);

        var root = Path.GetFullPath(workspaceRoot);
        var combined = Path.IsPathRooted(solution)
            ? Path.GetFullPath(solution)
            : Path.GetFullPath(Path.Combine(root, solution.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)));

        if (!IsInsideWorkspace(root, combined))
        {
            throw new ProofConfigException($"analysis.solution '{solution}' must stay inside the workspace root.");
        }

        if (!File.Exists(combined))
        {
            throw new ProofConfigException($"analysis.solution not found: {solution}");
        }

        return combined;
    }

    public static void ValidateConfiguredPath(string solution)
    {
        var fileName = Path.GetFileName(solution.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or "..")
        {
            throw new ProofConfigException("analysis.solution must point to a .sln, .slnx, or .csproj file.");
        }

        var extension = Path.GetExtension(fileName);
        if (!SupportedExtensions.Contains(extension))
        {
            throw new ProofConfigException($"Unsupported analysis.solution extension '{extension}'. Use .sln, .slnx, or .csproj.");
        }
    }

    private static bool IsInsideWorkspace(string workspaceRoot, string candidate)
    {
        var relative = Path.GetRelativePath(workspaceRoot, candidate);
        return !string.IsNullOrEmpty(relative)
            && !Path.IsPathRooted(relative)
            && !relative.StartsWith("..", StringComparison.Ordinal);
    }
}
