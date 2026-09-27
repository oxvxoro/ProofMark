using System.Diagnostics;

namespace Distill.Runner;

public sealed class ProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;

        if (spec.StdoutPath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(spec.StdoutPath)!);
        }

        if (spec.StderrPath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(spec.StderrPath)!);
        }

        using var process = new Process
        {
            StartInfo = CreateStartInfo(spec),
            EnableRaisingEvents = true
        };

        try
        {
            if (!process.Start())
            {
                return new ProcessResult(
                    null,
                    DateTimeOffset.UtcNow - startedAt,
                    ProcessStatus.FailedToStart,
                    spec.StdoutPath,
                    spec.StderrPath,
                    "Process failed to start.");
            }
        }
        catch (Exception ex)
        {
            return new ProcessResult(
                null,
                DateTimeOffset.UtcNow - startedAt,
                ProcessStatus.FailedToStart,
                spec.StdoutPath,
                spec.StderrPath,
                ex.Message);
        }

        await using var stdoutWriter = spec.StdoutPath is null
            ? null
            : new StreamWriter(spec.StdoutPath, append: false) { AutoFlush = true };

        await using var stderrWriter = spec.StderrPath is null
            ? null
            : new StreamWriter(spec.StderrPath, append: false) { AutoFlush = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdoutWriter?.WriteLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderrWriter?.WriteLine(e.Data);
            }
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (spec.Timeout is { } timeout)
        {
            linkedCts.CancelAfter(timeout);
        }

        try
        {
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);

            return new ProcessResult(
                process.ExitCode,
                DateTimeOffset.UtcNow - startedAt,
                ProcessStatus.Completed,
                spec.StdoutPath,
                spec.StderrPath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            KillProcessTree(process);
            return new ProcessResult(
                null,
                DateTimeOffset.UtcNow - startedAt,
                ProcessStatus.Canceled,
                spec.StdoutPath,
                spec.StderrPath);
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            return new ProcessResult(
                null,
                DateTimeOffset.UtcNow - startedAt,
                ProcessStatus.TimedOut,
                spec.StdoutPath,
                spec.StderrPath,
                "Process timed out.");
        }
    }

    private static ProcessStartInfo CreateStartInfo(ProcessSpec spec)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = spec.FileName,
            WorkingDirectory = spec.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in spec.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (spec.Environment is not null)
        {
            foreach (var (key, value) in spec.Environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        return startInfo;
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // 최선의 정리.
        }
    }
}
