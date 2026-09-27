using Distill.Runner;

namespace Distill.Git;

public sealed record GitChangeSnapshot(
    string StatusText,
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyList<ChangedHunk> Hunks,
    string DiffPatch,
    bool IsAvailable = true,
    string? ErrorMessage = null,
    IReadOnlyList<string>? UntrackedFiles = null,
    bool UntrackedCaptureFailed = false,
    IReadOnlyList<GitFileChange>? FileChanges = null,
    bool StructuredDeltaFailed = false,
    bool WorkingTreeDirty = true);

internal delegate Task<ProcessResult> GitRunAsync(
    IReadOnlyList<string> arguments,
    string workingDirectory,
    string? stdoutPath,
    CancellationToken cancellationToken);

public sealed class ChangedFileCollector
{
    private readonly GitRunAsync _runGit;

    public ChangedFileCollector()
        : this(CreateDefaultRunner())
    {
    }

    internal ChangedFileCollector(GitRunAsync runGit)
    {
        _runGit = runGit;
    }

    private static GitRunAsync CreateDefaultRunner()
    {
        var git = new GitCommandRunner();
        return (arguments, workingDirectory, stdoutPath, cancellationToken) =>
            git.RunAsync(arguments, workingDirectory, stdoutPath, cancellationToken: cancellationToken);
    }

    public async Task<GitChangeSnapshot> CollectAsync(
        string workspaceRoot,
        string? statusPath = null,
        string? diffPath = null,
        string? baseRevision = null,
        CancellationToken cancellationToken = default)
    {
        var ownedDiff = false;
        var ownedStatus = false;
        if (string.IsNullOrWhiteSpace(diffPath))
        {
            diffPath = Path.Combine(Path.GetTempPath(), $"distill-git-diff-{Guid.NewGuid():N}.patch");
            ownedDiff = true;
        }

        if (string.IsNullOrWhiteSpace(statusPath))
        {
            statusPath = Path.Combine(Path.GetTempPath(), $"distill-git-status-{Guid.NewGuid():N}.txt");
            ownedStatus = true;
        }

        try
        {
            string statusText;
            bool workingTreeDirty;
            var statusResult = await RunGitAsync(
                ["status", "--porcelain"],
                workspaceRoot,
                statusPath,
                cancellationToken).ConfigureAwait(false);
            if (IsSuccessful(statusResult))
            {
                statusText = await ReadOutputAsync(statusPath, cancellationToken).ConfigureAwait(false);
                workingTreeDirty = !string.IsNullOrWhiteSpace(statusText);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(baseRevision))
                {
                    return CreateUnavailableSnapshot(
                        statusResult.ErrorMessage ?? "Git status command failed.");
                }

                statusText = string.Empty;
                workingTreeDirty = true;
            }

            var (diffPatch, diffSucceeded, diffError) = await CollectDiffPatchAsync(
                workspaceRoot,
                diffPath,
                baseRevision,
                cancellationToken).ConfigureAwait(false);

            if (!diffSucceeded)
            {
                return CreateUnavailableSnapshot(diffError ?? "Git diff command failed.");
            }

            List<string> changedFiles;
            try
            {
                changedFiles = !string.IsNullOrWhiteSpace(baseRevision)
                    ? await CollectChangedFilesFromBaseAsync(workspaceRoot, baseRevision, cancellationToken).ConfigureAwait(false)
                    : statusText
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(ParseStatusPath)
                        .Where(file => !string.IsNullOrWhiteSpace(file))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
            }
            catch (InvalidOperationException exception)
            {
                return CreateUnavailableSnapshot(exception.Message);
            }

            var (untracked, untrackedFailed) = await CollectUntrackedAsync(workspaceRoot, cancellationToken)
                .ConfigureAwait(false);

            foreach (var file in untracked)
            {
                if (!changedFiles.Contains(file, StringComparer.OrdinalIgnoreCase))
                {
                    changedFiles.Add(file);
                }
            }

            var hunks = DiffHunkParser.Parse(diffPatch);
            var (fileChanges, structuredFailed) = await CollectStructuredChangesAsync(
                workspaceRoot,
                baseRevision,
                changedFiles,
                hunks,
                diffPatch,
                cancellationToken).ConfigureAwait(false);

            return new GitChangeSnapshot(
                statusText,
                changedFiles,
                hunks,
                diffPatch,
                UntrackedFiles: untracked,
                UntrackedCaptureFailed: untrackedFailed,
                FileChanges: fileChanges,
                StructuredDeltaFailed: structuredFailed,
                WorkingTreeDirty: workingTreeDirty);
        }
        finally
        {
            if (ownedDiff && File.Exists(diffPath))
            {
                File.Delete(diffPath);
            }

            if (ownedStatus && statusPath is not null && File.Exists(statusPath))
            {
                File.Delete(statusPath);
            }
        }
    }

    private async Task<(IReadOnlyList<string> Files, bool Failed)> CollectUntrackedAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"distill-git-untracked-{Guid.NewGuid():N}.txt");
        try
        {
            var result = await RunGitAsync(
                ["ls-files", "-z", "--others", "--exclude-standard"],
                workspaceRoot,
                outputPath,
                cancellationToken).ConfigureAwait(false);

            if (!IsSuccessful(result))
            {
                return (Array.Empty<string>(), true);
            }

            var output = await ReadOutputAsync(outputPath, cancellationToken).ConfigureAwait(false);
            var files = output
                .Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(path => path.Replace('\\', '/'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return (files, false);
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    private async Task<List<string>> CollectChangedFilesFromBaseAsync(
        string workspaceRoot,
        string baseRevision,
        CancellationToken cancellationToken)
    {
        var nameOnlyPath = Path.Combine(Path.GetTempPath(), $"distill-git-names-{Guid.NewGuid():N}.txt");
        try
        {
            var result = await RunGitAsync(
                ["diff", "--name-only", "--find-renames", baseRevision],
                workspaceRoot,
                nameOnlyPath,
                cancellationToken).ConfigureAwait(false);

            if (!IsSuccessful(result))
            {
                throw new InvalidOperationException(result.ErrorMessage ?? "Git diff --name-only failed.");
            }

            var output = await ReadOutputAsync(nameOnlyPath, cancellationToken).ConfigureAwait(false);
            return output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(path => path.Replace('\\', '/'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally
        {
            if (File.Exists(nameOnlyPath))
            {
                File.Delete(nameOnlyPath);
            }
        }
    }

    private async Task<(string DiffPatch, bool Succeeded, string? ErrorMessage)> CollectDiffPatchAsync(
        string workspaceRoot,
        string? diffPath,
        string? baseRevision,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(baseRevision))
        {
            var result = await RunGitAsync(
                ["diff", "--find-renames", "--binary", "--unified=3", baseRevision],
                workspaceRoot,
                diffPath,
                cancellationToken).ConfigureAwait(false);

            if (!IsSuccessful(result))
            {
                var error = result.ErrorMessage
                    ?? (result.Status == ProcessStatus.TimedOut
                        ? "Git diff command timed out."
                        : "Git diff command failed.");
                return (string.Empty, false, error);
            }

            var patch = await ReadOutputAsync(diffPath, cancellationToken).ConfigureAwait(false);
            return (patch, true, null);
        }

        var attempts = new IReadOnlyList<string>[]
        {
            ["diff", "--find-renames", "--binary", "--unified=3", "HEAD"],
            ["diff", "--find-renames", "--binary", "--unified=3"],
            ["diff", "--cached", "--find-renames", "--binary", "--unified=3"]
        };

        string? lastError = null;
        var acceptedAnySuccessfulDiff = false;
        var diffPatch = string.Empty;

        foreach (var arguments in attempts)
        {
            if (acceptedAnySuccessfulDiff && !string.IsNullOrWhiteSpace(diffPatch))
            {
                break;
            }

            var result = await RunGitAsync(arguments, workspaceRoot, diffPath, cancellationToken)
                .ConfigureAwait(false);

            if (!IsSuccessful(result))
            {
                lastError = result.ErrorMessage
                    ?? (result.Status == ProcessStatus.TimedOut
                        ? "Git diff command timed out."
                        : "Git diff command failed.");
                continue;
            }

            acceptedAnySuccessfulDiff = true;
            diffPatch = await ReadOutputAsync(diffPath, cancellationToken).ConfigureAwait(false);
        }

        return (diffPatch, acceptedAnySuccessfulDiff, lastError);
    }

    private async Task<ProcessResult> RunGitAsync(
        IReadOnlyList<string> arguments,
        string workspaceRoot,
        string? stdoutPath,
        CancellationToken cancellationToken)
    {
        var result = await _runGit(arguments, workspaceRoot, stdoutPath, cancellationToken)
            .ConfigureAwait(false);

        if (result.Status == ProcessStatus.Canceled)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        return result;
    }

    private static bool IsSuccessful(ProcessResult result)
        => result.Status == ProcessStatus.Completed && result.ExitCode == 0;

    private static async Task<string> ReadOutputAsync(string? path, CancellationToken cancellationToken)
    {
        if (path is not null && File.Exists(path))
        {
            return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }

        return string.Empty;
    }

    private static GitChangeSnapshot CreateUnavailableSnapshot(string errorMessage)
        => new(
            string.Empty,
            Array.Empty<string>(),
            Array.Empty<ChangedHunk>(),
            string.Empty,
            IsAvailable: false,
            ErrorMessage: errorMessage);

    private async Task<(IReadOnlyList<GitFileChange> Changes, bool StructuredFailed)> CollectStructuredChangesAsync(
        string workspaceRoot,
        string? baseRevision,
        IReadOnlyList<string> changedFiles,
        IReadOnlyList<ChangedHunk> hunks,
        string diffPatch,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(baseRevision))
        {
            var raw = await CollectRawAsync(workspaceRoot, baseRevision, cancellationToken).ConfigureAwait(false);
            if (raw.Count > 0)
            {
                return (AnnotateBinaryAndSubmodule(raw, diffPatch), false);
            }

            var parsed = await CollectNameStatusAsync(workspaceRoot, baseRevision, cancellationToken)
                .ConfigureAwait(false);
            if (parsed.Count > 0)
            {
                return (AnnotateBinaryAndSubmodule(parsed, diffPatch), false);
            }
        }

        var inferred = InferFromChangedFiles(changedFiles, hunks, diffPatch);
        return (inferred, changedFiles.Count > 0);
    }

    private async Task<IReadOnlyList<GitFileChange>> CollectRawAsync(
        string workspaceRoot,
        string baseRevision,
        CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"distill-git-raw-{Guid.NewGuid():N}.txt");
        try
        {
            var result = await RunGitAsync(
                ["diff", "--raw", "-z", "--find-renames", baseRevision],
                workspaceRoot,
                outputPath,
                cancellationToken).ConfigureAwait(false);
            if (!IsSuccessful(result))
            {
                return [];
            }

            var output = await ReadOutputAsync(outputPath, cancellationToken).ConfigureAwait(false);
            return ParseDiffRawZ(output);
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    private async Task<IReadOnlyList<GitFileChange>> CollectNameStatusAsync(
        string workspaceRoot,
        string baseRevision,
        CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"distill-git-name-status-{Guid.NewGuid():N}.txt");
        try
        {
            var result = await RunGitAsync(
                ["diff", "--name-status", "-z", "--find-renames", baseRevision],
                workspaceRoot,
                outputPath,
                cancellationToken).ConfigureAwait(false);
            if (!IsSuccessful(result))
            {
                return [];
            }

            var output = await ReadOutputAsync(outputPath, cancellationToken).ConfigureAwait(false);
            return ParseNameStatusZ(output);
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    public static IReadOnlyList<GitFileChange> ParseNameStatusZ(string output)
    {
        var tokens = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var changes = new List<GitFileChange>();
        for (var index = 0; index < tokens.Length; index++)
        {
            var status = tokens[index].Trim();
            if (status.Length == 0)
            {
                continue;
            }

            var code = status[0];
            if ((code is 'R' or 'C') && index + 2 < tokens.Length)
            {
                var oldPath = tokens[++index].Replace('\\', '/');
                var newPath = tokens[++index].Replace('\\', '/');
                changes.Add(new GitFileChange(GitFileChangeKind.Renamed, oldPath, newPath));
                continue;
            }

            if (index + 1 >= tokens.Length)
            {
                break;
            }

            var path = tokens[++index].Replace('\\', '/');
            var kind = code switch
            {
                'A' => GitFileChangeKind.Added,
                'D' => GitFileChangeKind.Deleted,
                'M' or 'T' => GitFileChangeKind.Modified,
                _ => GitFileChangeKind.Modified
            };
            changes.Add(new GitFileChange(kind, kind == GitFileChangeKind.Added ? null : path, kind == GitFileChangeKind.Deleted ? null : path));
        }

        return changes;
    }

    public static IReadOnlyList<GitFileChange> ParseDiffRawZ(string output)
    {
        var tokens = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var changes = new List<GitFileChange>();
        for (var index = 0; index < tokens.Length; index++)
        {
            var header = tokens[index].Trim();
            if (header.Length == 0 || header[0] != ':')
            {
                continue;
            }

            var parts = header[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5)
            {
                continue;
            }

            var srcMode = parts[0];
            var dstMode = parts[1];
            var status = parts[4];
            var code = status[0];
            string? oldPath;
            string? newPath;
            if ((code is 'R' or 'C') && index + 2 < tokens.Length)
            {
                oldPath = tokens[++index].Replace('\\', '/');
                newPath = tokens[++index].Replace('\\', '/');
            }
            else if (index + 1 < tokens.Length)
            {
                var path = tokens[++index].Replace('\\', '/');
                oldPath = code == 'A' ? null : path;
                newPath = code == 'D' ? null : path;
            }
            else
            {
                break;
            }

            GitFileChangeKind kind;
            if (srcMode == "120000" || dstMode == "120000")
            {
                kind = GitFileChangeKind.Unsupported;
            }
            else if (srcMode == "160000" || dstMode == "160000")
            {
                kind = GitFileChangeKind.SubmoduleChanged;
            }
            else
            {
                kind = code switch
                {
                    'A' => GitFileChangeKind.Added,
                    'D' => GitFileChangeKind.Deleted,
                    'R' => GitFileChangeKind.Renamed,
                    'C' => GitFileChangeKind.Renamed,
                    'M' or 'T' => GitFileChangeKind.Modified,
                    _ => GitFileChangeKind.Modified
                };
            }

            changes.Add(new GitFileChange(kind, oldPath, newPath));
        }

        return changes;
    }

    private static IReadOnlyList<GitFileChange> AnnotateBinaryAndSubmodule(
        IReadOnlyList<GitFileChange> changes,
        string diffPatch)
    {
        return changes.Select(change =>
        {
            if (change.Kind is GitFileChangeKind.Unsupported or GitFileChangeKind.SubmoduleChanged)
            {
                return change;
            }

            if (IsSubmoduleDiff(diffPatch, change.OldPath, change.NewPath))
            {
                return change with { Kind = GitFileChangeKind.SubmoduleChanged };
            }

            if (IsBinaryDiff(diffPatch, change.OldPath, change.NewPath))
            {
                return change with { Kind = GitFileChangeKind.BinaryModified, IsBinary = true };
            }

            return change;
        }).ToArray();
    }

    private static IReadOnlyList<GitFileChange> InferFromChangedFiles(
        IReadOnlyList<string> changedFiles,
        IReadOnlyList<ChangedHunk> hunks,
        string diffPatch)
    {
        return changedFiles.Select(file =>
        {
            var path = file.Replace('\\', '/');
            if (IsSubmoduleDiff(diffPatch, path, path))
            {
                return new GitFileChange(GitFileChangeKind.SubmoduleChanged, path, path);
            }

            if (IsBinaryDiff(diffPatch, path, path))
            {
                return new GitFileChange(GitFileChangeKind.BinaryModified, path, path, IsBinary: true);
            }

            return new GitFileChange(GitFileChangeKind.Modified, path, path);
        }).ToArray();
    }

    internal static bool IsBinaryDiff(string diffPatch, string? oldPath, string? newPath)
    {
        if (string.IsNullOrWhiteSpace(diffPatch))
        {
            return false;
        }

        // 패치 전체의 binary 표식은 다른 파일을 오염시킨다.
        // 이 파일의 diff --git 구간 안에서만 판정한다.
        var inSection = false;
        foreach (var raw in diffPatch.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                if (inSection)
                {
                    return false;
                }

                inSection = HeaderMatchesFile(line, oldPath, newPath);
                continue;
            }

            if (!inSection)
            {
                continue;
            }

            if (line.StartsWith("GIT binary patch", StringComparison.Ordinal)
                || line.StartsWith("Binary files ", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HeaderMatchesFile(string line, string? oldPath, string? newPath)
    {
        if (!TryParseDiffGitHeader(line, out var headerOld, out var headerNew))
        {
            return false;
        }

        var oldNormalized = NormalizeDiffPath(oldPath);
        var newNormalized = NormalizeDiffPath(newPath);
        if (oldNormalized is not null && newNormalized is not null)
        {
            return headerOld == oldNormalized && headerNew == newNormalized;
        }

        if (newNormalized is not null)
        {
            return headerNew == newNormalized || headerOld == newNormalized;
        }

        return oldNormalized is not null && (headerOld == oldNormalized || headerNew == oldNormalized);
    }

    private static bool TryParseDiffGitHeader(string line, out string oldPath, out string newPath)
    {
        oldPath = string.Empty;
        newPath = string.Empty;
        const string prefix = "diff --git ";
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = line[prefix.Length..];
        if (!TryReadDiffToken(rest, out var oldToken, out var consumed))
        {
            return false;
        }

        rest = rest[consumed..].TrimStart();
        if (!TryReadDiffToken(rest, out var newToken, out _))
        {
            return false;
        }

        return TryStripDiffSide(oldToken, 'a', out oldPath)
            && TryStripDiffSide(newToken, 'b', out newPath);
    }

    private static bool TryReadDiffToken(string text, out string token, out int consumed)
    {
        token = string.Empty;
        consumed = 0;
        if (text.Length == 0)
        {
            return false;
        }

        if (text[0] != '"')
        {
            var space = text.IndexOf(' ');
            token = space < 0 ? text : text[..space];
            consumed = token.Length;
            return token.Length > 0;
        }

        var builder = new System.Text.StringBuilder();
        for (var index = 1; index < text.Length; index++)
        {
            var current = text[index];
            if (current == '\\' && index + 1 < text.Length)
            {
                builder.Append(text[index + 1]);
                index++;
                continue;
            }

            if (current == '"')
            {
                token = builder.ToString();
                consumed = index + 1;
                return true;
            }

            builder.Append(current);
        }

        return false;
    }

    private static bool TryStripDiffSide(string token, char side, out string path)
    {
        var prefix = $"{side}/";
        if (!token.StartsWith(prefix, StringComparison.Ordinal))
        {
            path = string.Empty;
            return false;
        }

        path = token[prefix.Length..];
        return true;
    }

    private static string? NormalizeDiffPath(string? path)
        => string.IsNullOrWhiteSpace(path) ? null : path.Replace('\\', '/');

    internal static bool IsSubmoduleDiff(string diffPatch, string? oldPath, string? newPath)
    {
        if (string.IsNullOrWhiteSpace(diffPatch))
        {
            return false;
        }

        // 소스에 들어 있는 "160000" 문자열은 서브모듈 모드가 아니다.
        // 이 파일 구간의 인덱스 모드나 Subproject commit만 본다.
        var inSection = false;
        foreach (var raw in diffPatch.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                if (inSection)
                {
                    return false;
                }

                inSection = HeaderMatchesFile(line, oldPath, newPath);
                continue;
            }

            if (!inSection)
            {
                continue;
            }

            var body = line.Length > 0 && line[0] is '+' or '-' or ' ' ? line[1..] : null;
            if (body is not null && body.StartsWith("Subproject commit ", StringComparison.Ordinal))
            {
                return true;
            }

            if (body is null && line.EndsWith(" 160000", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static string ParseStatusPath(string line)
    {
        if (line.Length <= 3)
        {
            return line.Trim();
        }

        var path = line[3..].Trim();
        var renameSeparator = path.LastIndexOf(" -> ", StringComparison.Ordinal);
        if (renameSeparator >= 0)
        {
            path = path[(renameSeparator + 4)..].Trim();
        }

        return path.Trim('"');
    }
}
