using Distill.Core.Paths;
using Distill.Core.Planning;

namespace Proof.Adapters.Distill;

internal static class DistillCommandRewriter
{
    internal static string SplitBaseId(string checkId)
    {
        var separator = checkId.IndexOf("::", StringComparison.Ordinal);
        return separator < 0 ? checkId : checkId[..separator];
    }

    internal static string RewriteCommand(
        string command,
        string workspaceRoot,
        string? overrideTarget,
        string? testFilter,
        string? coverageResultsDirectory = null)
    {
        var parseResult = DotnetCommandParser.Parse(command);
        var target = parseResult.Target;
        if (!string.IsNullOrWhiteSpace(overrideTarget))
        {
            var resolved = ResolveProjectPath(workspaceRoot, overrideTarget!);
            if (resolved is not null)
            {
                target = DistillPath.ForCommandArgument(resolved);
            }
            else
            {
                // 해석할 수 없는 override는 조용히 솔루션 전체 실행으로
                // 내려가서는 안 된다. 그러면 정확한 증거인 척하게 된다.
                throw new DistillVerificationRunner.OverrideResolutionException(
                    $"Override target '{overrideTarget}' could not be resolved to a project in '{workspaceRoot}'.");
            }
        }

        var isTestVerb = parseResult.Verb.Equals("test", StringComparison.OrdinalIgnoreCase);
        var arguments = parseResult.Arguments.ToList();
        if (!string.IsNullOrWhiteSpace(testFilter) && isTestVerb)
        {
            arguments.Add("--filter");
            arguments.Add("FullyQualifiedName~" + testFilter);
        }

        if (!string.IsNullOrWhiteSpace(coverageResultsDirectory) && isTestVerb)
        {
            // 따옴표는 실행기가 다시 파싱할 때 "XPlat Code Coverage"를
            // 하나의 인자로 유지한다.
            arguments.Add("--collect:\"XPlat Code Coverage\"");
            arguments.Add("--results-directory:" + DistillPath.ForCommandArgument(coverageResultsDirectory!));
        }

        var rewritten = parseResult with { Target = target, Arguments = arguments };
        return "dotnet " + string.Join(' ', DotnetCommandParser.ToArgumentList(rewritten));
    }

    internal static bool IsTestVerb(string command)
    {
        try
        {
            return DotnetCommandParser.Parse(command).Verb.Equals("test", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    internal static string? ResolveProjectPath(string workspaceRoot, string overrideTarget)
    {
        if (overrideTarget.StartsWith("scip:", StringComparison.OrdinalIgnoreCase))
        {
            var packageRoot = overrideTarget["scip:".Length..].Trim();
            if (string.IsNullOrWhiteSpace(packageRoot))
            {
                return workspaceRoot;
            }

            var scipRoot = Path.IsPathRooted(packageRoot)
                ? packageRoot
                : Path.Combine(workspaceRoot, packageRoot.Replace('/', Path.DirectorySeparatorChar));
            return Directory.Exists(scipRoot) ? overrideTarget : null;
        }

        var candidate = Path.IsPathRooted(overrideTarget)
            ? overrideTarget
            : Path.Combine(workspaceRoot, overrideTarget);
        if (File.Exists(candidate))
        {
            return overrideTarget;
        }

        // 점이 있는 프로젝트 이름(예: "Proof.Core")을
        // Path.GetFileNameWithoutExtension이 파일명 + 확장자로 다루어서는 안 된다.
        var name = overrideTarget.Replace('\\', '/').Split('/')[^1].TrimEnd();
        if (!Directory.Exists(workspaceRoot))
        {
            return null;
        }

        string? best = null;
        foreach (var extension in (string[]) [".csproj", ".fsproj"])
        {
            foreach (var path in Directory.EnumerateFiles(workspaceRoot, "*" + extension, SearchOption.AllDirectories))
            {
                var normalized = path.Replace('\\', '/');
                if (normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.Equals(Path.GetFileNameWithoutExtension(path), name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (best is null
                    || path.Length < best.Length
                    || (path.Length == best.Length && string.CompareOrdinal(path, best) < 0))
                {
                    best = path;
                }
            }
        }

        return best;
    }
}
