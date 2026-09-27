namespace Distill.Mcp;

/// <summary>
/// MCP 도구가 요청한 루트를 서버 시작 시 고정한 워크스페이스 안으로만 제한한다.
/// </summary>
internal sealed class McpWorkspacePolicy
{
    private readonly string? _pinnedRoot;

    internal McpWorkspacePolicy(string? pinnedRoot, string? defaultRoot = null)
    {
        var effective = string.IsNullOrWhiteSpace(pinnedRoot) ? defaultRoot : pinnedRoot;
        _pinnedRoot = string.IsNullOrWhiteSpace(effective) ? null : Path.GetFullPath(effective);
    }

    internal bool TryResolve(string? requestedRoot, out string resolvedRoot, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(requestedRoot))
        {
            resolvedRoot = _pinnedRoot ?? Directory.GetCurrentDirectory();
            return true;
        }

        string candidate;
        try
        {
            candidate = Path.GetFullPath(requestedRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            resolvedRoot = string.Empty;
            error = $"Rejected root '{requestedRoot}': not a valid path.";
            return false;
        }

        if (_pinnedRoot is not null && !IsWithin(_pinnedRoot, candidate))
        {
            resolvedRoot = string.Empty;
            error = $"Rejected root '{requestedRoot}': outside the pinned workspace root '{_pinnedRoot}'.";
            return false;
        }

        resolvedRoot = candidate;
        return true;
    }

    internal static bool IsWithin(string root, string candidate)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedRoot = TrimTrailingSeparator(Path.GetFullPath(root));
        var normalizedCandidate = TrimTrailingSeparator(Path.GetFullPath(candidate));
        if (string.Equals(normalizedRoot, normalizedCandidate, comparison))
        {
            return true;
        }

        var relative = Path.GetRelativePath(normalizedRoot, normalizedCandidate);
        return !relative.StartsWith("..", comparison) && !Path.IsPathRooted(relative);
    }

    private static string TrimTrailingSeparator(string path)
        => path.Length > 1 ? Path.TrimEndingDirectorySeparator(path) : path;
}
