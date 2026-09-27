using Distill.Core.Abstractions;
using Distill.Core.Config;
using Distill.Core.Planning;
using Distill.Core.Redaction;
using Distill.Core.Runs;
using Distill.Git;

namespace Distill.Reporting;

/// <summary>
/// 검사 실행 후 Failure Pack, normalized.json, run manifest, latest 상태를 기록한다.
/// Distill CLI verify 경로와 Proof의 프로세스 내 Distill 러너가 공유한다.
/// </summary>
public static class DistillRunArtifactPublisher
{
    private static readonly DistillationPipeline Pipeline = new();

    public static async Task PublishAsync(
        string workspaceRoot,
        DistillConfig config,
        string profileName,
        DistillRunContext context,
        IReadOnlyList<CheckRunResult> checkResults,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken,
        ReportOptions? reportOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(checkResults);

        var runDirectory = context.RunDirectory;
        Directory.CreateDirectory(runDirectory);

        await SnapshotConfigAsync(config, runDirectory, cancellationToken).ConfigureAwait(false);

        GitChangeSnapshot gitSnapshot;
        try
        {
            gitSnapshot = await new GitEvidenceCollector()
                .CollectAsync(context, baseRevision: null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            gitSnapshot = new GitChangeSnapshot(
                string.Empty,
                Array.Empty<string>(),
                Array.Empty<ChangedHunk>(),
                string.Empty,
                IsAvailable: false,
                ErrorMessage: ex.Message);
        }

        var rawStatus = CheckExecutorCoordinator.ResolveOverallStatus(checkResults);
        var effectiveOptions = (reportOptions ?? new ReportOptions()) with
        {
            RedactionPatterns = config.Redaction.Patterns,
            MinConfidence = config.Reliability.MinConfidence
        };
        var pack = VerificationReportBuilder.Build(
            rawStatus,
            context,
            checkResults,
            gitSnapshot,
            effectiveOptions);

        var normalized = Pipeline.Normalize(
            pack.Status,
            checkResults,
            checkResults.SelectMany(check => check.Diagnostics).ToList(),
            gitSnapshot,
            context.WorkspaceRoot);
        await Pipeline.WriteNormalizedAsync(
            normalized,
            runDirectory,
            gitSnapshot,
            cancellationToken,
            new SecretRedactor(config.Redaction.Patterns)).ConfigureAwait(false);
        await AtomicFileWriter.WriteTextAsync(
            RunArtifactLayout.GetFailurePackPath(runDirectory),
            pack.CompactText,
            cancellationToken).ConfigureAwait(false);

        var finishedAt = DateTimeOffset.UtcNow;
        var rawBytes = GetDirectoryBytes(runDirectory);
        var compactBytes = System.Text.Encoding.UTF8.GetByteCount(pack.CompactText);
        await RunManifestWriter.WriteAsync(
            runDirectory,
            new RunManifest(
                context.RunId,
                startedAt,
                workspaceRoot,
                finishedAt,
                pack.Status,
                rawBytes,
                compactBytes),
            cancellationToken).ConfigureAwait(false);

        await DistillStateStore.SaveLatestAsync(
            workspaceRoot,
            new DistillState(context.RunId, runDirectory, finishedAt),
            cancellationToken).ConfigureAwait(false);
        RunRetention.Prune(workspaceRoot, protectedRunId: context.RunId);
    }

    private static long GetDirectoryBytes(string directory)
        => Directory
            .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path).Length)
            .Sum();

    private static async Task SnapshotConfigAsync(
        DistillConfig config,
        string runDirectory,
        CancellationToken cancellationToken)
    {
        var yaml = DistillConfigLoader.Serialize(config);
        await AtomicFileWriter.WriteTextAsync(
            RunArtifactLayout.GetConfigSnapshotPath(runDirectory),
            yaml,
            cancellationToken).ConfigureAwait(false);
    }
}
