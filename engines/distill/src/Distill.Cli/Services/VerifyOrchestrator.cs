using Distill.Core.Abstractions;
using Distill.Core.Config;
using Distill.Core.Planning;
using Distill.Core.Redaction;
using Distill.Core.Runs;
using Distill.Git;
using Distill.Reporting;
using Distill.Execution;
using Distill.Testing.VSTest;

namespace Distill.Cli.Services;

public sealed record VerifyResult(
    VerificationRun Run,
    VerificationPack Pack,
    int ExitCode);

public sealed class VerifyOrchestrator
{
    private readonly GitEvidenceCollector _gitCollector = new();

    public async Task<VerifyResult> RunAsync(
        string workspaceRoot,
        DistillConfig config,
        string profileName,
        ReportOptions reportOptions,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var runId = RunIdGenerator.Create(startedAt);
        var runDirectory = RunArtifactLayout.GetRunDirectory(workspaceRoot, runId);

        Directory.CreateDirectory(runDirectory);
        await SnapshotConfigAsync(config, runDirectory, cancellationToken).ConfigureAwait(false);

        var context = new DistillRunContext
        {
            RunId = runId,
            WorkspaceRoot = workspaceRoot,
            RunDirectory = runDirectory,
            Profile = profileName
        };

        await RunManifestWriter.WriteAsync(
            runDirectory,
            new RunManifest(runId, startedAt, workspaceRoot),
            cancellationToken).ConfigureAwait(false);

        try
        {
            GitChangeSnapshot gitSnapshot;
            try
            {
                gitSnapshot = await _gitCollector.CollectAsync(context, baseRevision: null, cancellationToken).ConfigureAwait(false);
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

            var plannedChecks = DistillVerifySession.Plan(config, profileName);
            if (plannedChecks.Count == 0)
            {
                throw new InvalidOperationException($"Profile '{profileName}' has no planned checks.");
            }

            var executor = DistillVerifySession.CreateExecutor(
                TestLoggerPathResolver.ResolveExtensionDirectory(),
                config.Testing.Platform);
            var checkResults = await DistillVerifySession.ExecuteDependencyAwareAsync(
                executor,
                plannedChecks,
                context,
                cancellationToken).ConfigureAwait(false);

            var rawStatus = CheckExecutorCoordinator.ResolveOverallStatus(checkResults);
            var effectiveOptions = reportOptions with
            {
                RedactionPatterns = config.Redaction.Patterns,
                MinConfidence = config.Reliability.MinConfidence
            };
            await DistillVerifySession.PublishArtifactsAsync(
                workspaceRoot,
                config,
                profileName,
                context,
                checkResults,
                startedAt,
                effectiveOptions,
                cancellationToken).ConfigureAwait(false);

            var pack = VerificationReportBuilder.Build(
                rawStatus,
                context,
                checkResults,
                gitSnapshot,
                effectiveOptions);

            var finishedAt = DateTimeOffset.UtcNow;
            var run = new VerificationRun
            {
                RunId = runId,
                Profile = profileName,
                Status = pack.Status,
                StartedAt = startedAt,
                FinishedAt = finishedAt,
                Checks = checkResults,
                Diagnostics = pack.Diagnostics
            };

            return new VerifyResult(run, pack, ExitCodeMapper.FromStatus(pack.Status));
        }
        catch (OperationCanceledException)
        {
            await TryFinalizeInfraErrorManifestAsync(
                runDirectory,
                runId,
                startedAt,
                workspaceRoot).ConfigureAwait(false);
            throw;
        }
        catch (Exception)
        {
            await TryFinalizeInfraErrorManifestAsync(
                runDirectory,
                runId,
                startedAt,
                workspaceRoot).ConfigureAwait(false);

            throw;
        }
    }

    private static async Task TryFinalizeInfraErrorManifestAsync(
        string runDirectory,
        string runId,
        DateTimeOffset startedAt,
        string workspaceRoot)
    {
        try
        {
            await RunManifestWriter.WriteAsync(
                runDirectory,
                new RunManifest(
                    runId,
                    startedAt,
                    workspaceRoot,
                    DateTimeOffset.UtcNow,
                    VerificationStatus.InfraError),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // 최선의 마무리일 뿐이며, 아래의 원래 예외가 권위 있다.
        }
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
