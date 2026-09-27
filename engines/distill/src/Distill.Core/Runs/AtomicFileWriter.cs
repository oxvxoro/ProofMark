using System.Collections.Concurrent;

namespace Distill.Core.Runs;

public static class AtomicFileWriter
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ProcessLocks = new(StringComparer.OrdinalIgnoreCase);

    public static async Task WriteTextAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        await UpdateTextAsync(
            path,
            _ => content,
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task UpdateTextAsync(
        string path,
        Func<string?, string?> transform,
        CancellationToken cancellationToken = default)
    {
        var processLock = ProcessLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await processLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var crossProcessLock = await AcquireCrossProcessLockAsync(
                path + ".lock",
                cancellationToken).ConfigureAwait(false);

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string? currentText = null;
            if (File.Exists(path))
            {
                currentText = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            }

            var replacement = transform(currentText);
            if (replacement is null)
            {
                return;
            }

            var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(tempPath, replacement, cancellationToken).ConfigureAwait(false);
                PublishWithRetry(tempPath, path);
            }
            catch
            {
                TryDelete(tempPath);
                throw;
            }
        }
        finally
        {
            processLock.Release();
        }
    }

    private static async Task<CrossProcessFileLock> AcquireCrossProcessLockAsync(
        string lockPath,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var stream = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
                return new CrossProcessFileLock(stream);
            }
            catch (IOException)
            {
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new TimeoutException($"Timed out waiting for file lock '{lockPath}'.");
    }

    private static void PublishWithRetry(string tempPath, string destinationPath)
    {
        Exception? lastException = null;
        for (var attempt = 0; attempt < 50; attempt++)
        {
            try
            {
                if (File.Exists(destinationPath))
                {
                    File.Move(tempPath, destinationPath, overwrite: true);
                }
                else
                {
                    File.Move(tempPath, destinationPath);
                }

                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastException = ex;
                Thread.Sleep(Math.Min(50, 5 * (attempt + 1)));
            }
        }

        throw lastException ?? new IOException($"Failed to publish '{destinationPath}'.");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class CrossProcessFileLock(FileStream stream) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            stream.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
