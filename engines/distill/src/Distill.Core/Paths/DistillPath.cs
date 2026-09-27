namespace Distill.Core.Paths;

public static class DistillPath
{
    public static string CanonicalizeSeparators(string path)
        => path.Replace('\\', '/');

    public static bool IsWindowsDriveAbsolute(ReadOnlySpan<char> path)
        => path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':';

    public static bool IsPosixAbsolute(ReadOnlySpan<char> path)
        => path.Length > 0 && (path[0] == '/' || path[0] == '\\');

    public static bool IsVirtuallyAbsolute(string path)
        => IsWindowsDriveAbsolute(path) || IsPosixAbsolute(path);

    public static string ForCommandArgument(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (IsVirtuallyAbsolute(path))
        {
            return CanonicalizeSeparators(path);
        }

        return Path.GetFullPath(path).Replace('\\', '/');
    }

    public static bool Equivalent(string left, string right, string? workspaceRoot = null)
        => string.Equals(ToIdentity(left, workspaceRoot), ToIdentity(right, workspaceRoot), StringComparison.OrdinalIgnoreCase);

    public static string ToIdentity(string path, string? workspaceRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return TrimRelativePrefix(CanonicalizeSeparators(path), keepPosixRoot: false);
        }

        if (CanUseHostFileSystem(path, workspaceRoot))
        {
            return ToHostRelativeIdentity(path, workspaceRoot);
        }

        return ToVirtualIdentity(path, workspaceRoot);
    }

    private static bool CanUseHostFileSystem(string path, string workspaceRoot)
    {
        if (IsWindowsDriveAbsolute(path)
            && IsPosixAbsolute(workspaceRoot)
            && !IsWindowsDriveAbsolute(workspaceRoot))
        {
            return false;
        }

        if (IsWindowsDriveAbsolute(path) || IsWindowsDriveAbsolute(workspaceRoot))
        {
            return OperatingSystem.IsWindows();
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.IsPathFullyQualified(workspaceRoot);
        }

        return Path.IsPathRooted(workspaceRoot);
    }

    private static string ToHostRelativeIdentity(string path, string workspaceRoot)
    {
        var root = Path.GetFullPath(workspaceRoot).Replace('\\', '/').TrimEnd('/');
        var fullPath = Path.IsPathRooted(path)
            ? Path.GetFullPath(path).Replace('\\', '/')
            : Path.GetFullPath(Path.Combine(workspaceRoot, path)).Replace('\\', '/');

        if (TryMakeRelative(fullPath, root, out var relative))
        {
            return relative;
        }

        return fullPath;
    }

    private static string ToVirtualIdentity(string path, string workspaceRoot)
    {
        var root = TrimRelativePrefix(CanonicalizeSeparators(workspaceRoot), keepPosixRoot: true).TrimEnd('/');
        var canonical = TrimRelativePrefix(CanonicalizeSeparators(path), keepPosixRoot: true);
        if (IsWindowsDriveAbsolute(canonical)
            && TryMakeRelative("/" + canonical[2..].TrimStart('/'), root, out var driveRelative))
        {
            return driveRelative;
        }

        var full = IsVirtuallyAbsolute(canonical)
            ? canonical
            : string.IsNullOrEmpty(canonical)
                ? root
                : $"{root}/{canonical.TrimStart('/')}";

        if (TryMakeRelative(full, root, out var relative))
        {
            return relative;
        }

        return full;
    }

    private static bool TryMakeRelative(string fullPath, string root, out string relative)
    {
        if (string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
        {
            relative = string.Empty;
            return true;
        }

        var prefix = root + "/";
        if (fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            relative = fullPath[prefix.Length..];
            return true;
        }

        relative = fullPath;
        return false;
    }

    private static string TrimRelativePrefix(string normalized, bool keepPosixRoot)
    {
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        if (keepPosixRoot || IsWindowsDriveAbsolute(normalized) || !normalized.StartsWith('/'))
        {
            return normalized;
        }

        return normalized.TrimStart('/');
    }
}
