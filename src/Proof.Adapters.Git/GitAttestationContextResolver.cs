using Proof.Core;

namespace Proof.Adapters.Git;

/// <summary>
/// 증명 출처 해석기. 워크스페이스에서 git을 실행해 origin 저장소와
/// 현재 브랜치를 해석한다. CI 환경 재정의
/// (GITHUB_REPOSITORY / GITHUB_REF_NAME)가 있으면 그것이 이긴다.
/// </summary>
public sealed class GitAttestationContextResolver : IAttestationContextResolver
{
    public async Task<AttestationContext> ResolveAsync(
        string workspaceRoot,
        string runId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var repository = ResolveRepository(workspaceRoot);
        var branch = ResolveBranch(workspaceRoot);
        var reference = ResolveRef(branch);
        return new AttestationContext(
            runId,
            repository,
            branch,
            reference,
            FromEnvironment("GITHUB_WORKFLOW_REF"),
            FromEnvironment("GITHUB_WORKFLOW_SHA"),
            ResolveCommit(workspaceRoot),
            FromEnvironment("GITHUB_RUN_ATTEMPT"));
    }

    private static string? FromEnvironment(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? ResolveRef(string? branch)
    {
        var fromEnvironment = FromEnvironment("GITHUB_REF");
        if (fromEnvironment is not null)
        {
            return fromEnvironment;
        }

        return string.IsNullOrWhiteSpace(branch) ? null : "refs/heads/" + branch;
    }

    private static string? ResolveCommit(string workspaceRoot)
    {
        var fromEnvironment = FromEnvironment("GITHUB_SHA");
        if (fromEnvironment is not null)
        {
            return fromEnvironment;
        }

        var sha = RunGitOutput(workspaceRoot, "rev-parse HEAD");
        return string.IsNullOrWhiteSpace(sha) ? null : sha.Trim();
    }

    private static string? ResolveRepository(string workspaceRoot)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        var url = RunGitOutput(workspaceRoot, "config --get remote.origin.url");
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        // "git@github.com:owner/repo.git"과 https 형태를 owner/repo로 정규화한다.
        var value = url.Trim().TrimEnd('/');
        if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^4];
        }

        var scheme = value.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            value = value[(scheme + 3)..];
        }

        if (value.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
        {
            value = value[4..];
        }

        return value.Replace(':', '/');
    }

    private static string? ResolveBranch(string workspaceRoot)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("GITHUB_REF_NAME");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        var branch = RunGitOutput(workspaceRoot, "rev-parse --abbrev-ref HEAD");
        return string.IsNullOrWhiteSpace(branch) ? null : branch.Trim();
    }

    private static string? RunGitOutput(string workspaceRoot, string arguments)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workspaceRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }
}
