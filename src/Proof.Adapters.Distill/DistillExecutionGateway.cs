using Distill.Core.Config;
using Distill.Core.Planning;
using Distill.Core.Runs;
using Distill.Execution;
using Proof.Core;

namespace Proof.Adapters.Distill;

internal sealed record DistillRunOutput(
    IReadOnlyList<ProofEvidence> Evidence,
    IReadOnlyList<string> CoverageArtifactPaths);

internal static class DistillExecutionGateway
{
    internal static async Task<DistillRunOutput> RunAsync(
        ProofPlan proofPlan,
        VerificationPlan verificationPlan,
        string workspaceRoot,
        string? distillConfigPath,
        CancellationToken cancellationToken,
        ImportedCheckManifest? importManifest = null)
    {
        var configPath = DistillCapabilitySource.ResolveConfigPath(workspaceRoot, distillConfigPath);
        if (!File.Exists(configPath))
        {
            throw new ProofConfigException($"{ProofReasonCodes.DistillConfigNotFound}: {configPath}");
        }

        var config = DistillConfigLoader.Load(configPath);

        // 런타임 커버리지는 검증 계획마다 선택이다. 그때만 테스트
        // 명령이 이 실행 전용 결과 디렉터리로 커버리지를 수집한다.
        var coverageRequested = DistillExecutionPlanCompiler.RequestsRuntimeCoverage(verificationPlan);
        var runId = RunIdGenerator.Create();
        var runDirectory = RunArtifactLayout.GetRunDirectory(workspaceRoot, runId);
        var startedAt = DateTimeOffset.UtcNow;
        var coverageDirectory = coverageRequested
            ? Path.Combine(runDirectory, "coverage")
            : null;

        IReadOnlyList<PlannedCheck> plannedChecks;
        try
        {
            plannedChecks = DistillExecutionPlanCompiler.ResolvePlannedChecks(
                config, verificationPlan, workspaceRoot, coverageDirectory);
        }
        catch (KeyNotFoundException exception)
        {
            throw new ProofConfigException(exception.Message, exception);
        }

        Directory.CreateDirectory(runDirectory);
        var context = new DistillRunContext
        {
            RunId = runId,
            WorkspaceRoot = workspaceRoot,
            RunDirectory = runDirectory,
            Profile = verificationPlan.Profile
        };

        // 같은 검사를 다시 실행하기보다 완전히 검증된 가져온 매니페스트를
        // 우선한다. 일부만 있거나 어긋난 매니페스트는 통째로 무시하여,
        // 오래된 바이트를 믿지 않고 검사를 정직하게 다시 실행한다.
        IReadOnlyList<CheckRunResult> results;
        var imported = false;
        if (importManifest is not null
            && ImportedCheckResolver.TryResolve(
                proofPlan,
                plannedChecks,
                workspaceRoot,
                importManifest,
                out var importedResults))
        {
            imported = true;
            results = importedResults;
        }
        else
        {
            var executor = DistillVerifySession.CreateExecutor();
            results = plannedChecks.Count == 0
                ? Array.Empty<CheckRunResult>()
                : await DistillVerifySession.ExecuteDependencyAwareAsync(
                    executor,
                    plannedChecks,
                    context,
                    cancellationToken).ConfigureAwait(false);
        }

        var evidence = DistillEvidenceMapper.Map(proofPlan, plannedChecks, results);

        // 커버리지 산출물은 의도적으로 테스트 증거 스트림에 섞지 않는다.
        // 오케스트레이터가 커버리지 생산자에게 넘긴다. XML을 내지 않은 실행은
        // 빈 목록을 준다(지어낸 Pass는 결코 아니다).
        var coverageArtifactPaths = coverageDirectory is null
            ? []
            : CollectCoverageArtifacts(workspaceRoot, coverageDirectory);
        if (!imported)
        {
            coverageArtifactPaths = coverageArtifactPaths
                .Concat(CollectConfiguredCoverage(workspaceRoot, plannedChecks, results, startedAt))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        // 가져온 결과는 이 실행이 아니라 CI가 만들었다. 우리가 소유하지
        // 않은 산출물을 다시 게시하지 않는다.
        if (!imported && results.Count > 0)
        {
            await DistillVerifySession.PublishArtifactsAsync(
                workspaceRoot,
                config,
                verificationPlan.Profile,
                context,
                results,
                startedAt,
                reportOptions: null,
                cancellationToken).ConfigureAwait(false);
        }

        return new DistillRunOutput(evidence, coverageArtifactPaths);
    }

    /// <summary>
    /// kind: process 검사에 명시된 Cobertura 경로. 이 실행에서 검사가 끝났고
    /// 파일이 실행 중에 쓰였을 때만 넘긴다. 없거나 이전 파일이면 넘기지 않아
    /// 해당 P005는 미해결로 남는다.
    /// </summary>
    internal static IReadOnlyList<string> CollectConfiguredCoverage(
        string workspaceRoot,
        IReadOnlyList<PlannedCheck> plannedChecks,
        IReadOnlyList<CheckRunResult> results,
        DateTimeOffset startedAt)
    {
        var ran = results
            .Where(result => result.Status is VerificationStatus.Pass or VerificationStatus.Fail or VerificationStatus.Uncertain)
            .Select(result => result.CheckId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var paths = new List<string>();
        foreach (var planned in plannedChecks)
        {
            if (!string.Equals(planned.Definition.Kind, "process", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(planned.Definition.Coverage)
                || !ran.Contains(planned.Id))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(Path.Combine(workspaceRoot, planned.Definition.Coverage));
            if (!File.Exists(fullPath)
                || File.GetLastWriteTimeUtc(fullPath) < startedAt.UtcDateTime.AddSeconds(-2))
            {
                continue;
            }

            paths.Add(Path.GetRelativePath(workspaceRoot, fullPath).Replace('\\', '/'));
        }

        return paths;
    }

    internal static IReadOnlyList<string> CollectCoverageArtifacts(string workspaceRoot, string coverageDirectory)
    {
        if (!Directory.Exists(coverageDirectory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(coverageDirectory, "coverage.cobertura.xml", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(workspaceRoot, path).Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }
}
