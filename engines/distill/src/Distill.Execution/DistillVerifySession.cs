using Distill.Core.Abstractions;
using Distill.Core.Config;
using Distill.Core.Planning;
using Distill.Core.Runs;
using Distill.Reporting;

namespace Distill.Execution;

public static class DistillVerifySession
{
    public static IReadOnlyList<PlannedCheck> Plan(DistillConfig config, string profileName) =>
        CheckPlanner.Plan(config, profileName);

    public static DistillCheckExecutor CreateExecutor(
        string? loggerExtensionDirectory = null,
        string configuredPlatform = "auto") =>
        DistillCheckExecutorFactory.Create(loggerExtensionDirectory, configuredPlatform);

    public static Task<IReadOnlyList<CheckRunResult>> ExecuteDependencyAwareAsync(
        ICheckExecutor executor,
        IReadOnlyList<PlannedCheck> plannedChecks,
        DistillRunContext context,
        CancellationToken cancellationToken) =>
        CheckExecutorCoordinator.ExecuteDependencyAwareAsync(
            executor,
            plannedChecks,
            context,
            cancellationToken);

    public static Task PublishArtifactsAsync(
        string workspaceRoot,
        DistillConfig config,
        string profileName,
        DistillRunContext context,
        IReadOnlyList<CheckRunResult> checkResults,
        DateTimeOffset startedAt,
        ReportOptions? reportOptions,
        CancellationToken cancellationToken) =>
        DistillRunArtifactPublisher.PublishAsync(
            workspaceRoot,
            config,
            profileName,
            context,
            checkResults,
            startedAt,
            cancellationToken,
            reportOptions);
}
