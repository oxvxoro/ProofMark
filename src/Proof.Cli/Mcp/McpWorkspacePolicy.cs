namespace Proof.Cli;

/// <summary>
/// 도구 실행을 워크스페이스 루트에 고정한다. 도구 인자로 요청된 루트는
/// 고정된 루트 안에 있을 때만 받아들인다. 그래서 MCP 클라이언트가
/// Proof를 임의의 디렉터리로 향하게 할 수 없다. 빈 요청 루트는
/// 고정 값(고정이 없으면 프로세스 디렉터리)으로 해석된다.
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

    /// <summary>
    /// 도구가 읽으려는 파일 경로(예: 인증서
    /// 경로)를 검증한다. 상대 경로는 워크스페이스 루트 기준으로 해석한다.
    /// 어느 쪽이든 결과는 고정된 루트 안에 있어야 한다. 클라이언트가
    /// 읽기 도구를 워크스페이스 밖의 임의 파일로 향하게 할 수 없다.
    /// </summary>
    internal bool TryResolveContained(string? requestedPath, string root, out string? resolvedPath, out string? error)
    {
        error = null;
        resolvedPath = null;
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            return true;
        }

        string candidate;
        try
        {
            candidate = Path.IsPathRooted(requestedPath)
                ? Path.GetFullPath(requestedPath)
                : Path.GetFullPath(Path.Combine(root, requestedPath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"Rejected path '{requestedPath}': not a valid path.";
            return false;
        }

        if (!IsWithin(root, candidate))
        {
            error = $"Rejected path '{requestedPath}': outside the pinned workspace root '{root}'.";
            return false;
        }

        resolvedPath = candidate;
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
