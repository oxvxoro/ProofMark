using Distill.Git;
using Proof.Core;

namespace Proof.Adapters.Git;

public static class GitChangeMapper
{
    public static SourceSnapshot ToSnapshot(
        string workspaceRoot,
        string baseRevision,
        string headRevision,
        GitChangeSnapshot gitSnapshot)
    {
        ArgumentNullException.ThrowIfNull(gitSnapshot);
        var files = new List<FileDelta>();
        var usedFallback = false;
        var hashCaptureFailed = false;
        var hunksByFile = gitSnapshot.Hunks
            .GroupBy(hunk => hunk.File.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        if (gitSnapshot.FileChanges is { Count: > 0 })
        {
            foreach (var change in gitSnapshot.FileChanges)
            {
                files.Add(ToDelta(workspaceRoot, change, hunksByFile, ref usedFallback, ref hashCaptureFailed));
            }
        }
        else
        {
            foreach (var group in gitSnapshot.Hunks.GroupBy(hunk => hunk.File.Replace('\\', '/')))
            {
                files.Add(FromHunks(group.Key, group.ToArray(), ref usedFallback));
            }

            foreach (var changedFile in gitSnapshot.ChangedFiles)
            {
                var path = changedFile.Replace('\\', '/');
                if (files.Any(file => string.Equals(file.NewPath ?? file.OldPath, path, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                usedFallback = true;
                var hashed = TryHash(workspaceRoot, path, required: true, ref hashCaptureFailed);
                files.Add(new FileDelta(
                    FileChangeKind.Modified,
                    path,
                    path,
                    [],
                    [new LineSpan(path, 1, int.MaxValue)],
                    NewContentSha256: hashed));
            }
        }

        foreach (var untracked in gitSnapshot.UntrackedFiles ?? [])
        {
            var path = NormalizeRepositoryPath(workspaceRoot, untracked);
            if (path is null)
            {
                hashCaptureFailed = true;
                continue;
            }

            if (files.Any(file => string.Equals(file.NewPath, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var full = Path.Combine(workspaceRoot, path.Replace('/', Path.DirectorySeparatorChar));
            if (IsUnsupportedUntracked(full))
            {
                files.Add(new FileDelta(FileChangeKind.Unsupported, null, path, [], []));
                continue;
            }

            var hashed = TryHash(workspaceRoot, path, required: true, ref hashCaptureFailed);
            files.Add(new FileDelta(
                FileChangeKind.Added,
                null,
                path,
                [],
                [new LineSpan(path, 1, int.MaxValue)],
                NewContentSha256: hashed));
        }

        return SourceSnapshotCollector.Create(
            workspaceRoot,
            baseRevision,
            headRevision,
            files,
            gitSnapshot.UntrackedCaptureFailed,
            usedFallback,
            isDirty: gitSnapshot.WorkingTreeDirty,
            hashCaptureFailed,
            gitSnapshot.StructuredDeltaFailed);
    }

    private static FileDelta ToDelta(
        string workspaceRoot,
        GitFileChange change,
        IReadOnlyDictionary<string, ChangedHunk[]> hunksByFile,
        ref bool usedFallback,
        ref bool hashCaptureFailed)
    {
        var oldPath = change.OldPath?.Replace('\\', '/');
        var newPath = change.NewPath?.Replace('\\', '/');
        ChangedHunk[] hunks = [];
        if (newPath is not null && hunksByFile.TryGetValue(newPath, out var byNew))
        {
            hunks = byNew;
        }
        else if (oldPath is not null && hunksByFile.TryGetValue(oldPath, out var byOld))
        {
            hunks = byOld;
        }

        var kind = change.Kind switch
        {
            GitFileChangeKind.Added => FileChangeKind.Added,
            GitFileChangeKind.Deleted => FileChangeKind.Deleted,
            GitFileChangeKind.Renamed => FileChangeKind.Renamed,
            GitFileChangeKind.BinaryModified => FileChangeKind.BinaryModified,
            GitFileChangeKind.SubmoduleChanged => FileChangeKind.SubmoduleChanged,
            GitFileChangeKind.Unsupported => FileChangeKind.Unsupported,
            _ => FileChangeKind.Modified
        };

        var oldSpans = hunks
            .Where(hunk => hunk.OldLength > 0)
            .Select(hunk => new LineSpan(oldPath ?? hunk.File, hunk.OldStart, hunk.OldStart + Math.Max(hunk.OldLength, 1) - 1))
            .ToArray();
        var newSpans = hunks
            .Where(hunk => hunk.NewLength > 0)
            .Select(hunk => new LineSpan(newPath ?? hunk.File, hunk.NewStart, hunk.NewStart + Math.Max(hunk.NewLength, 1) - 1))
            .ToArray();

        if (kind is FileChangeKind.Modified or FileChangeKind.Renamed or FileChangeKind.Added
            && newSpans.Length == 0
            && kind != FileChangeKind.Deleted
            && kind != FileChangeKind.BinaryModified
            && kind != FileChangeKind.SubmoduleChanged
            && kind != FileChangeKind.Unsupported)
        {
            usedFallback = true;
            var fallbackPath = newPath ?? oldPath ?? string.Empty;
            newSpans = [new LineSpan(fallbackPath, 1, int.MaxValue)];
        }

        var requireNewHash = kind is FileChangeKind.Added or FileChangeKind.Modified or FileChangeKind.Renamed;
        return new FileDelta(
            kind,
            oldPath,
            newPath,
            oldSpans,
            newSpans,
            OldContentSha256: null,
            NewContentSha256: TryHash(workspaceRoot, newPath, requireNewHash, ref hashCaptureFailed));
    }

    private static FileDelta FromHunks(string path, ChangedHunk[] hunks, ref bool usedFallback)
    {
        var newSpans = hunks
            .Select(hunk => new LineSpan(path, hunk.NewStart, hunk.NewStart + Math.Max(hunk.NewLength, 1) - 1))
            .ToArray();
        var oldSpans = hunks
            .Where(hunk => hunk.OldLength > 0)
            .Select(hunk => new LineSpan(path, hunk.OldStart, hunk.OldStart + Math.Max(hunk.OldLength, 1) - 1))
            .ToArray();
        if (newSpans.Length == 0)
        {
            usedFallback = true;
            newSpans = [new LineSpan(path, 1, int.MaxValue)];
        }

        return new FileDelta(FileChangeKind.Modified, path, path, oldSpans, newSpans);
    }

    private static bool IsUnsupportedUntracked(string fullPath)
    {
        try
        {
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            {
                return false;
            }

            var attributes = File.GetAttributes(fullPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }

            return File.ResolveLinkTarget(fullPath, returnFinalTarget: false) is not null;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? NormalizeRepositoryPath(string workspaceRoot, string relative)
    {
        var combined = Path.GetFullPath(Path.Combine(workspaceRoot, relative.Replace('\\', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(workspaceRoot);
        var relativePath = Path.GetRelativePath(root, combined);
        if (Path.IsPathRooted(relativePath) || relativePath.StartsWith("..", StringComparison.Ordinal))
        {
            return null;
        }

        return relativePath.Replace('\\', '/');
    }

    private static string? TryHash(string workspaceRoot, string? relativePath, bool required, ref bool hashCaptureFailed)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            if (required)
            {
                hashCaptureFailed = true;
            }

            return null;
        }

        var full = Path.Combine(workspaceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full))
        {
            if (required)
            {
                hashCaptureFailed = true;
            }

            return null;
        }

        try
        {
            return SourceSnapshotHasher.HashFileContent(full);
        }
        catch (IOException)
        {
            if (required)
            {
                hashCaptureFailed = true;
            }

            return null;
        }
    }
}
