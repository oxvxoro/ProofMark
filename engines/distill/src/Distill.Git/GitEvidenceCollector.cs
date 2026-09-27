using Distill.Core.Runs;

namespace Distill.Git;

public sealed class GitEvidenceCollector
{
    private readonly ChangedFileCollector _collector = new();

    public async Task<GitChangeSnapshot> CollectAsync(
        DistillRunContext context,
        string? baseRevision = null,
        CancellationToken cancellationToken = default)
    {
        var gitDirectory = RunArtifactLayout.GetGitDirectory(context.RunDirectory);
        Directory.CreateDirectory(gitDirectory);

        return await _collector.CollectAsync(
            context.WorkspaceRoot,
            RunArtifactLayout.GetGitStatusPath(context.RunDirectory),
            RunArtifactLayout.GetGitDiffPath(context.RunDirectory),
            baseRevision,
            cancellationToken).ConfigureAwait(false);
    }
}
