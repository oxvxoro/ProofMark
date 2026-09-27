using Distill.Core.Config;
using Distill.Core.Planning;
using Proof.Core;

namespace Proof.Adapters.Distill;

internal static class DistillExecutionPlanCompiler
{
    internal static bool RequestsRuntimeCoverage(VerificationPlan verificationPlan)
        => verificationPlan.Checks.Any(entry =>
            DistillVerificationRunner.IsRuntimeCoverageCheckId(DistillCommandRewriter.SplitBaseId(entry.CheckId)));

    internal static IReadOnlyList<PlannedCheck> ResolvePlannedChecks(
        DistillConfig config,
        VerificationPlan verificationPlan,
        string workspaceRoot,
        string? coverageResultsDirectory = null)
    {
        if (verificationPlan.Checks.Count == 0)
        {
            return [];
        }

        var catalog = CheckPlanner.Plan(config, verificationPlan.Profile);
        var catalogById = catalog
            .Select(item => (item.Id, item))
            .DistinctBy(pair => pair.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(pair => pair.Id, pair => pair.item, StringComparer.OrdinalIgnoreCase);

        var resolved = new List<PlannedCheck>();
        foreach (var entry in verificationPlan.Checks)
        {
            var baseId = DistillCommandRewriter.SplitBaseId(entry.CheckId);
            if (!catalogById.TryGetValue(baseId, out var baseCheck))
            {
                continue;
            }

            // process 명령은 dotnet 명령이 아니므로 재작성하지 않는다. 범위를 다시 잡은
            // 클론은 base 검사와 같은 명령이라 base만 실행한다.
            if (string.Equals(baseCheck.Definition.Kind, "process", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(entry.CheckId, baseId, StringComparison.Ordinal))
                {
                    resolved.Add(baseCheck);
                }

                continue;
            }

            // 커버리지 수집은 평범한 base 검사에도 재작성으로 친다.
            // 테스트 명령은 Cobertura XML을 내려면 --collect를 실어야 한다.
            var collectCoverage = coverageResultsDirectory is not null
                && DistillCommandRewriter.IsTestVerb(baseCheck.Definition.Command);

            if (string.Equals(entry.CheckId, baseId, StringComparison.Ordinal)
                && string.IsNullOrWhiteSpace(entry.OverrideTarget)
                && string.IsNullOrWhiteSpace(entry.TestFilter)
                && !collectCoverage)
            {
                resolved.Add(baseCheck);
                continue;
            }

            string command;
            try
            {
                command = DistillCommandRewriter.RewriteCommand(
                    baseCheck.Definition.Command,
                    workspaceRoot,
                    entry.OverrideTarget,
                    entry.TestFilter,
                    coverageResultsDirectory);
            }
            catch (DistillVerificationRunner.OverrideResolutionException)
            {
                // 해석할 수 없는 override는 조용히 저장소 전체 실행으로
                // 내려가서는 안 된다. 클론을 건너뛰어 의무가 정직하게
                // 미충족으로 남게 한다(REQUIRED_EVIDENCE_CAPABILITY_MISSING).
                // 가짜 정확 증거를 만들지 않기 위해서다.
                continue;
            }

            var rewritten = ReferenceEquals(command, baseCheck.Definition.Command)
                ? baseCheck.Definition
                : new CheckConfig
                {
                    Kind = baseCheck.Definition.Kind,
                    Command = command,
                    Source = baseCheck.Definition.Source,
                    Timeout = baseCheck.Definition.Timeout,
                    StopOnFailure = baseCheck.Definition.StopOnFailure,
                    DependsOn = [.. baseCheck.Definition.DependsOn]
                };
            resolved.Add(new PlannedCheck(
                entry.CheckId,
                rewritten,
                entry.DependsOn ?? baseCheck.DependsOn));
        }

        return resolved;
    }
}
