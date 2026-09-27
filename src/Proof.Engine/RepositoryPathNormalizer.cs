namespace Proof.Engine;

internal static class RepositoryPathNormalizer
{
    internal static string Normalize(string? path, string? workspaceRoot = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = path.Replace('\\', '/');
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            try
            {
                var root = Path.GetFullPath(workspaceRoot).Replace('\\', '/').TrimEnd('/');
                var combined = Path.IsPathRooted(path)
                    ? Path.GetFullPath(path)
                    : Path.GetFullPath(Path.Combine(workspaceRoot, path.Replace('/', Path.DirectorySeparatorChar)));
                var full = combined.Replace('\\', '/');
                if (full.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
                {
                    normalized = full[(root.Length + 1)..];
                }
                else
                {
                    normalized = full;
                }
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or IOException)
            {
                normalized = path.Replace('\\', '/');
            }
        }

        normalized = StripDrivePrefix(normalized);
        return StripDistillRunSegment(normalized).Trim('/');
    }

    private static string StripDrivePrefix(string path)
    {
        if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':')
        {
            path = path[2..];
        }

        return path.TrimStart('/');
    }

    private static string StripDistillRunSegment(string path)
    {
        const string marker = ".distill/runs/";
        var index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return path;
        }

        var prefix = ".distill/";
        var after = path[(index + marker.Length)..];
        var slash = after.IndexOf('/');
        return slash < 0 ? ".distill" : prefix + after[(slash + 1)..];
    }
}
