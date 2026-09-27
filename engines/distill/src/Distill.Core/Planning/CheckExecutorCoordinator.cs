using Distill.Core.Diagnostics;
using Distill.Core.Runs;

namespace Distill.Core.Planning;

public sealed record CheckRunResult(
    string CheckId,
    string Kind,
    VerificationStatus Status,
    int? ExitCode,
    IReadOnlyList<DistillDiagnostic> Diagnostics,
    string? SourceId,
    string? ArtifactPointer,
    TimeSpan Duration,
    IReadOnlyList<Distill.Core.Evidence.TestCaseEvidence>? ExecutedCases = null);

public interface ICheckExecutor
{
    Task<CheckRunResult> ExecuteAsync(
        PlannedCheck check,
        DistillRunContext context,
        CancellationToken cancellationToken);
}

public static class CheckExecutorCoordinator
{
    public static Task<IReadOnlyList<CheckRunResult>> ExecuteSequentialAsync(
        ICheckExecutor executor,
        IReadOnlyList<PlannedCheck> checks,
        DistillRunContext context,
        CancellationToken cancellationToken)
        => ExecuteDependencyAwareAsync(executor, checks, context, cancellationToken, maxParallelism: 1);

    public static async Task<IReadOnlyList<CheckRunResult>> ExecuteDependencyAwareAsync(
        ICheckExecutor executor,
        IReadOnlyList<PlannedCheck> checks,
        DistillRunContext context,
        CancellationToken cancellationToken,
        int maxParallelism = int.MaxValue)
    {
        if (maxParallelism <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxParallelism),
                maxParallelism,
                "maxParallelism must be greater than zero.");
        }

        var resultsById = new Dictionary<string, CheckRunResult>(StringComparer.OrdinalIgnoreCase);
        var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inFlight = new Dictionary<string, Task<CheckRunResult>>(StringComparer.OrdinalIgnoreCase);
        var stopScheduling = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!stopScheduling)
            {
                var ready = checks
                    .Where(check => !completed.Contains(check.Id)
                                    && !inFlight.ContainsKey(check.Id)
                                    && check.DependsOn.All(completed.Contains)
                                    && check.DependsOn.All(dependencyId =>
                                        resultsById.TryGetValue(dependencyId, out var dependencyResult)
                                        && dependencyResult.Status == VerificationStatus.Pass))
                    .Take(maxParallelism - inFlight.Count)
                    .ToList();

                foreach (var check in ready)
                {
                    inFlight[check.Id] = ExecuteCheckAsync(executor, check, context, cancellationToken);
                }
            }

            if (inFlight.Count == 0)
            {
                break;
            }

            var finished = await Task.WhenAny(inFlight.Values).ConfigureAwait(false);
            var finishedEntry = inFlight.First(pair => pair.Value == finished);
            inFlight.Remove(finishedEntry.Key);

            var result = await finishedEntry.Value.ConfigureAwait(false);
            resultsById[finishedEntry.Key] = result;
            completed.Add(finishedEntry.Key);

            var plannedCheck = checks.First(check => string.Equals(check.Id, finishedEntry.Key, StringComparison.OrdinalIgnoreCase));
            if (plannedCheck.Definition.StopOnFailure
                && result.Status is VerificationStatus.Fail or VerificationStatus.InfraError)
            {
                stopScheduling = true;
            }
        }

        var ordered = new List<CheckRunResult>(checks.Count);
        foreach (var check in checks)
        {
            if (!resultsById.TryGetValue(check.Id, out var result))
            {
                result = CreateSyntheticBlockedResult(check, resultsById, stopScheduling);
                resultsById[check.Id] = result;
            }

            ordered.Add(result);
        }

        return ordered;
    }

    private static CheckRunResult CreateSyntheticBlockedResult(
        PlannedCheck check,
        IReadOnlyDictionary<string, CheckRunResult> resultsById,
        bool stopScheduling)
    {
        var blockedByDependency = check.DependsOn.Any(dependencyId =>
            resultsById.TryGetValue(dependencyId, out var dependencyResult)
            && dependencyResult.Status != VerificationStatus.Pass);

        var code = blockedByDependency ? "CHECK_BLOCKED" : "CHECK_NOT_SCHEDULED";
        var message = blockedByDependency
            ? $"Check '{check.Id}' was not scheduled because a dependency did not pass."
            : $"Check '{check.Id}' was not scheduled because verification stopped scheduling new work.";

        var diagnostic = DistillDiagnostic.Create(
            id: $"{check.Id}-synthetic",
            kind: DiagnosticKind.Infrastructure,
            severity: DiagnosticSeverity.Warning,
            source: "distill",
            code: code,
            message: message,
            provenance: DiagnosticProvenance.RawFallback,
            confidence: 1.0);

        return new CheckRunResult(
            check.Id,
            check.Definition.Kind,
            VerificationStatus.Uncertain,
            null,
            new[] { diagnostic },
            "distill",
            null,
            TimeSpan.Zero);
    }

    private static async Task<CheckRunResult> ExecuteCheckAsync(
        ICheckExecutor executor,
        PlannedCheck check,
        DistillRunContext context,
        CancellationToken cancellationToken)
    {
        if (await CheckResultCache.TryRestoreAsync(check, context, cancellationToken).ConfigureAwait(false) is { } cached)
        {
            return cached;
        }

        var result = await executor.ExecuteAsync(check, context, cancellationToken).ConfigureAwait(false);
        await CheckResultCache.TryStoreAsync(check, context, result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public static VerificationStatus ResolveOverallStatus(IReadOnlyList<CheckRunResult> results)
    {
        if (results.Any(result => result.Status == VerificationStatus.InfraError))
        {
            return VerificationStatus.InfraError;
        }

        if (results.Any(result => result.Status == VerificationStatus.Fail))
        {
            return VerificationStatus.Fail;
        }

        if (results.Any(result => result.Status == VerificationStatus.Uncertain))
        {
            return VerificationStatus.Uncertain;
        }

        return VerificationStatus.Pass;
    }
}
