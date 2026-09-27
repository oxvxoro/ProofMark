using Distill.Git;
using Proof.Core;

namespace Proof.Adapters.Git;

public sealed class GitSourceSnapshotCollector : ISourceSnapshotCollector
{
    private readonly ChangedFileCollector _gitCollector;

    public GitSourceSnapshotCollector(ChangedFileCollector? gitCollector = null)
    {
        _gitCollector = gitCollector ?? new ChangedFileCollector();
    }

    public async Task<SourceSnapshot> CollectAsync(
        string workspaceRoot,
        string baseRevision,
        string headRevision,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseRevision);
        ArgumentException.ThrowIfNullOrWhiteSpace(headRevision);

        var gitSnapshot = await _gitCollector.CollectAsync(
            workspaceRoot,
            baseRevision: baseRevision,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!gitSnapshot.IsAvailable)
        {
            throw new ProofConfigException(gitSnapshot.ErrorMessage ?? "Git evidence collection failed.");
        }

        var snapshot = GitChangeMapper.ToSnapshot(workspaceRoot, baseRevision, headRevision, gitSnapshot);
        return await EnrichOldContentHashesAsync(workspaceRoot, baseRevision, snapshot, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<SourceSnapshot> EnrichOldContentHashesAsync(
        string workspaceRoot,
        string baseRevision,
        SourceSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var files = new List<FileDelta>(snapshot.Files.Count);
        var hashFailed = snapshot.ContentHashCaptureFailed;
        var pending = snapshot.Files
            .Where(file => file.Kind is FileChangeKind.Deleted or FileChangeKind.Modified or FileChangeKind.Renamed
                && !string.IsNullOrWhiteSpace(file.OldPath))
            .Select(file => file.OldPath!.Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var hashes = pending.Length == 0
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : await HashBaseBlobsAsync(workspaceRoot, baseRevision, pending, cancellationToken).ConfigureAwait(false);
        if (hashes is null)
        {
            // 배치 조회가 통째로 실패했다. ContentHashCaptureFailed가
            // CHANGE_CAPTURE_CONTENT_HASH_FAILED로 플래너에 보이게 다시 만든다.
            return RebuildWithHashFailure(snapshot);
        }

        foreach (var file in snapshot.Files)
        {
            var needsOld = file.Kind is FileChangeKind.Deleted or FileChangeKind.Modified or FileChangeKind.Renamed
                && !string.IsNullOrWhiteSpace(file.OldPath);
            if (!needsOld)
            {
                files.Add(file);
                continue;
            }

            if (!hashes.TryGetValue(file.OldPath!.Replace('\\', '/'), out var hash) || hash is null)
            {
                hashFailed = true;
                files.Add(file);
                continue;
            }

            files.Add(file with { OldContentSha256 = hash });
        }

        return SourceSnapshotCollector.Create(
            snapshot.RepositoryRoot,
            snapshot.BaseCommitSha,
            snapshot.HeadCommitSha,
            files,
            snapshot.UntrackedCaptureFailed,
            snapshot.UsedFileWideFallback,
            snapshot.IsDirty,
            hashFailed,
            snapshot.StructuredDeltaFailed);
    }

    internal static SourceSnapshot RebuildWithHashFailure(SourceSnapshot snapshot)
        => SourceSnapshotCollector.Create(
            snapshot.RepositoryRoot,
            snapshot.BaseCommitSha,
            snapshot.HeadCommitSha,
            snapshot.Files,
            snapshot.UntrackedCaptureFailed,
            snapshot.UsedFileWideFallback,
            snapshot.IsDirty,
            contentHashCaptureFailed: true,
            snapshot.StructuredDeltaFailed);

    private static async Task<Dictionary<string, string?>?> HashBaseBlobsAsync(
        string workspaceRoot,
        string baseRevision,
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
            // `git cat-file --batch`는 blob 내용 전체를 그대로 내므로
            // OS 파이프 버퍼를 넘겨 쓴 다음 읽는 순서가 교착될 수 있다.
            using var stdout = process.StandardOutput.BaseStream;
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            // stdin을 쓰기 전에 stdout 소비를 시작한다. 큰 응답이
            // 파이프를 채워 git의 stdin 리더를 막지 않게 한다.
            var hashesTask = GitCatFileBatchHasher.HashAsync(stdout, relativePaths, cancellationToken);

            foreach (var path in relativePaths)
            {
                await process.StandardInput.WriteLineAsync($"{baseRevision}:{path}").ConfigureAwait(false);
            }

            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            process.StandardInput.Close();

            var hashes = await hashesTask.ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await stderrTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                return null;
            }

            return hashes;
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
}
