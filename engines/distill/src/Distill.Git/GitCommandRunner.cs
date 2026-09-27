using Distill.Runner;

namespace Distill.Git;

public sealed class GitCommandRunner
{
    private readonly ProcessRunner _processRunner = new();

    public async Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string? stdoutPath = null,
        string? stderrPath = null,
        CancellationToken cancellationToken = default)
    {
        return await _processRunner.RunAsync(
            new ProcessSpec(
                FileName: "git",
                Arguments: arguments,
                WorkingDirectory: workingDirectory,
                Timeout: TimeSpan.FromSeconds(30),
                StdoutPath: stdoutPath,
                StderrPath: stderrPath),
            cancellationToken).ConfigureAwait(false);
    }
}
