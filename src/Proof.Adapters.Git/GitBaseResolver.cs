using Distill.Git;
using Distill.Runner;
using Proof.Core;

namespace Proof.Adapters.Git;

public static class GitBaseResolver
{
    public static async Task<string> ResolveAsync(
        string workspaceRoot,
        string? requestedRef,
        string? strategy,
        CancellationToken cancellationToken)
    {
        var requested = requestedRef;
        var git = new GitCommandRunner();
        if (string.IsNullOrWhiteSpace(requested))
        {
            requested = await ResolveOriginHeadAsync(git, workspaceRoot, cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(requested))
        {
            throw new ProofConfigException($"{ProofReasonCodes.BaseRefNotFound}: no base ref configured and origin/HEAD is unavailable.");
        }

        var sha = await RevParseAsync(git, workspaceRoot, requested, cancellationToken).ConfigureAwait(false);
        if (string.Equals(strategy, "mergeBase", StringComparison.OrdinalIgnoreCase))
        {
            var mergeBase = await TryMergeBaseAsync(git, workspaceRoot, sha, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(mergeBase))
            {
                return mergeBase;
            }
        }

        return sha;
    }

    public static async Task<string> ResolveHeadAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var git = new GitCommandRunner();
        return await RevParseAsync(git, workspaceRoot, "HEAD", cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> ResolveOriginHeadAsync(
        GitCommandRunner git,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var symbolic = await ReadAsync(git, workspaceRoot, ["rev-parse", "--abbrev-ref", "origin/HEAD"], cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(symbolic))
        {
            return symbolic.Trim();
        }

        foreach (var candidate in new[] { "origin/master", "origin/main", "master", "main" })
        {
            var sha = await TryRevParseAsync(git, workspaceRoot, candidate, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(sha))
            {
                return candidate;
            }
        }

        return null;
    }

    private static async Task<string> RevParseAsync(
        GitCommandRunner git,
        string workspaceRoot,
        string value,
        CancellationToken cancellationToken)
    {
        var sha = await TryRevParseAsync(git, workspaceRoot, value, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(sha))
        {
            throw new ProofConfigException($"{ProofReasonCodes.BaseRefNotFound}: '{value}'.");
        }

        return sha;
    }

    private static Task<string?> TryRevParseAsync(
        GitCommandRunner git,
        string workspaceRoot,
        string value,
        CancellationToken cancellationToken)
        => ReadAsync(git, workspaceRoot, ["rev-parse", "--verify", value], cancellationToken);

    private static Task<string?> TryMergeBaseAsync(
        GitCommandRunner git,
        string workspaceRoot,
        string baseSha,
        CancellationToken cancellationToken)
        => ReadAsync(git, workspaceRoot, ["merge-base", "HEAD", baseSha], cancellationToken);

    private static async Task<string?> ReadAsync(
        GitCommandRunner git,
        string workspaceRoot,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(Path.GetTempPath(), $"proof-git-{Guid.NewGuid():N}.txt");
        try
        {
            var result = await git.RunAsync(arguments, workspaceRoot, path, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (result.Status != ProcessStatus.Completed || result.ExitCode != 0 || !File.Exists(path))
            {
                return null;
            }

            var text = (await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)).Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
