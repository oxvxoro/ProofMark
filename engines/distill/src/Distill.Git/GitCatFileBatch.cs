namespace Distill.Git;

public static class GitCatFileBatch
{
    /// <summary>
    /// <c>git cat-file --batch</c> 한 번으로 blob을 읽는다.
    /// 배치 조회 자체가 실패하면 null을 반환한다. 없는 경로는 null에 대응한다.
    /// </summary>
    public static async Task<Dictionary<string, byte[]?>?> ReadAsync(
        string workspaceRoot,
        string revision,
        IReadOnlyList<string> relativePaths,
        CancellationToken cancellationToken)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workspaceRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("cat-file");
        startInfo.ArgumentList.Add("--batch");

        System.Diagnostics.Process? process;
        try
        {
            process = System.Diagnostics.Process.Start(startInfo);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }

        if (process is null)
        {
            return null;
        }

        try
        {
            // 경로를 쓰는 동안 stdout(과 stderr)을 동시에 비운다.
            // `git cat-file --batch`는 blob 내용 전체를 그대로 내보내며, 이는
            // OS 파이프 버퍼를 넘길 수 있다. Windows의 익명 파이프는 작고
            // 그에 따라 쓰고 나서 읽는 순서는 교착한다.
            using var stdout = process.StandardOutput.BaseStream;
            using var output = new MemoryStream();
            var stdoutTask = stdout.CopyToAsync(output, cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            foreach (var path in relativePaths)
            {
                await process.StandardInput.WriteLineAsync($"{revision}:{path}").ConfigureAwait(false);
            }

            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            process.StandardInput.Close();

            await stdoutTask.ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await stderrTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                return null;
            }

            return Parse(output.ToArray(), relativePaths);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException)
        {
            return null;
        }
        finally
        {
            process.Dispose();
        }
    }

    private static Dictionary<string, byte[]?> Parse(byte[] output, IReadOnlyList<string> relativePaths)
    {
        var result = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        var position = 0;
        foreach (var path in relativePaths)
        {
            var header = ReadLine(output, ref position);
            if (header is null)
            {
                result[path] = null;
                continue;
            }

            if (header.EndsWith(" missing", StringComparison.Ordinal)
                || header.EndsWith(" ambiguous", StringComparison.Ordinal))
            {
                result[path] = null;
                continue;
            }

            var parts = header.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !long.TryParse(parts[^1], out var size) || size < 0 || position + size > output.Length)
            {
                result[path] = null;
                continue;
            }

            var blob = new byte[(int)size];
            output.AsSpan(position, (int)size).CopyTo(blob);
            result[path] = blob;
            position += (int)size;
            if (position < output.Length && output[position] == (byte)'\n')
            {
                position++;
            }
        }

        return result;
    }

    private static string? ReadLine(byte[] buffer, ref int position)
    {
        if (position >= buffer.Length)
        {
            return null;
        }

        var end = Array.IndexOf(buffer, (byte)'\n', position);
        if (end < 0)
        {
            var rest = System.Text.Encoding.UTF8.GetString(buffer, position, buffer.Length - position);
            position = buffer.Length;
            return rest.TrimEnd('\r');
        }

        var line = System.Text.Encoding.UTF8.GetString(buffer, position, end - position).TrimEnd('\r');
        position = end + 1;
        return line;
    }
}
