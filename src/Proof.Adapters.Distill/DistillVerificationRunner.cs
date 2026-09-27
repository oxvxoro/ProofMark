using Distill.Core.Config;
using Distill.Core.Diagnostics;
using Distill.Core.Planning;
using Proof.Core;

namespace Proof.Adapters.Distill;

public sealed class DistillVerificationRunner : IVerificationRunner, IVerificationExecutor
{
    public const string RuntimeCoverageCheckId = EvidenceCheckIds.RuntimeCoverage;
    public const string ManualReviewCheckId = EvidenceCheckIds.ManualReview;

    private readonly string? _distillConfigPath;
    private readonly DistillVerificationCache _cache;
    private readonly string? _importManifestPath;

    public DistillVerificationRunner(
        string? distillConfigPath = null,
        bool cacheEnabled = false,
        string? importManifestPath = null)
    {
        _distillConfigPath = distillConfigPath;
        _cache = new DistillVerificationCache(cacheEnabled);
        _importManifestPath = importManifestPath;
    }

    public async Task<VerificationRunResult> VerifyAsync(
        ProofPlan plan,
        VerificationPlan verificationPlan,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var output = await RunVerificationAsync(plan, verificationPlan, workspaceRoot, _distillConfigPath, cancellationToken)
            .ConfigureAwait(false);
        // 링크는 의도적으로 비어 있다. 모든 생산자가 끝난 뒤
        // 오케스트레이터가 EvidenceBinder로 증거를 묶는다.
        return new VerificationRunResult(
            new VerificationEvidenceSet(output.Evidence, []),
            output.CoverageArtifactPaths);
    }

    public async Task<IReadOnlyList<ProofEvidence>> ExecuteAsync(
        ProofPlan proofPlan,
        VerificationPlan verificationPlan,
        string workspaceRoot,
        string? distillConfigPath,
        CancellationToken cancellationToken)
    {
        var output = await RunVerificationAsync(proofPlan, verificationPlan, workspaceRoot, distillConfigPath, cancellationToken)
            .ConfigureAwait(false);
        return output.Evidence;
    }

    private async Task<DistillRunOutput> RunVerificationAsync(
        ProofPlan proofPlan,
        VerificationPlan verificationPlan,
        string workspaceRoot,
        string? distillConfigPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proofPlan);
        ArgumentNullException.ThrowIfNull(verificationPlan);

        var importManifest = LoadImportManifest(workspaceRoot);

        // 가져오기 매니페스트는 증거 캐시에서 제외한다. 캐시된 실행이
        // 설정된 가져오기를 가려서는 안 되고, 그 반대도 마찬가지다.
        if (importManifest is null && _cache.IsCacheable(proofPlan, verificationPlan))
        {
            var cacheKey = DistillVerificationCache.ComputeCacheKey(proofPlan, verificationPlan);
            var cached = DistillVerificationCache.TryReadCache(workspaceRoot, cacheKey, proofPlan.SourceDigest);
            if (cached is not null)
            {
                // 캐시 적중이 이전 실행의 커버리지 산출물을 드러내서는 안 된다.
                return new DistillRunOutput(cached, []);
            }

            var runOutput = await DistillExecutionGateway.RunAsync(
                proofPlan,
                verificationPlan,
                workspaceRoot,
                distillConfigPath ?? _distillConfigPath,
                cancellationToken).ConfigureAwait(false);
            DistillVerificationCache.WriteCache(workspaceRoot, cacheKey, runOutput.Evidence);
            return runOutput;
        }

        return await DistillExecutionGateway.RunAsync(
            proofPlan,
            verificationPlan,
            workspaceRoot,
            distillConfigPath ?? _distillConfigPath,
            cancellationToken,
            importManifest).ConfigureAwait(false);
    }

    private ImportedCheckManifest? LoadImportManifest(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(_importManifestPath))
        {
            return null;
        }

        var path = Path.IsPathRooted(_importManifestPath)
            ? _importManifestPath
            : Path.Combine(workspaceRoot, _importManifestPath);
        return File.Exists(path) ? ImportedCheckManifestLoader.TryLoad(path) : null;
    }

    internal bool IsCacheable(ProofPlan proofPlan, VerificationPlan verificationPlan)
        => _cache.IsCacheable(proofPlan, verificationPlan);

    internal static string ComputeCacheKey(ProofPlan proofPlan, VerificationPlan verificationPlan)
        => DistillVerificationCache.ComputeCacheKey(proofPlan, verificationPlan);

    internal static IReadOnlyList<ProofEvidence>? TryReadCache(string workspaceRoot, string cacheKey, string? sourceDigest)
        => DistillVerificationCache.TryReadCache(workspaceRoot, cacheKey, sourceDigest);

    internal static void WriteCache(string workspaceRoot, string cacheKey, IReadOnlyList<ProofEvidence> evidence)
        => DistillVerificationCache.WriteCache(workspaceRoot, cacheKey, evidence);

    internal static IReadOnlyList<EvidenceSubjectRef> ResolveApiCompatProjectRefs(
        string? commandTarget,
        IReadOnlyList<DistillDiagnostic> diagnostics)
        => DistillEvidenceMapper.ResolveApiCompatProjectRefs(commandTarget, diagnostics);

    internal static IReadOnlyList<EvidenceSubjectRef> ResolveStaticAnalysisProjectRefs(
        string commandTargetProject,
        IReadOnlyList<DistillDiagnostic> diagnostics)
        => DistillEvidenceMapper.ResolveStaticAnalysisProjectRefs(commandTargetProject, diagnostics);

    internal static IReadOnlyList<string> CollectCoverageArtifacts(string workspaceRoot, string coverageDirectory)
        => DistillExecutionGateway.CollectCoverageArtifacts(workspaceRoot, coverageDirectory);

    internal static IReadOnlyList<PlannedCheck> ResolvePlannedChecks(
        DistillConfig config,
        VerificationPlan verificationPlan,
        string workspaceRoot,
        string? coverageResultsDirectory = null)
        => DistillExecutionPlanCompiler.ResolvePlannedChecks(config, verificationPlan, workspaceRoot, coverageResultsDirectory);

    public static string SplitBaseId(string checkId)
        => DistillCommandRewriter.SplitBaseId(checkId);

    internal static string RewriteCommand(
        string command,
        string workspaceRoot,
        string? overrideTarget,
        string? testFilter,
        string? coverageResultsDirectory = null)
        => DistillCommandRewriter.RewriteCommand(command, workspaceRoot, overrideTarget, testFilter, coverageResultsDirectory);

    internal static bool IsRuntimeCoverageCheckId(string checkId)
        => string.Equals(checkId, RuntimeCoverageCheckId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// OverrideTarget을 실제 프로젝트로 해석할 수 없을 때 던진다.
    /// 호출자는 원래의 저장소 전체 명령으로 물러가서는 안 된다.
    /// </summary>
    public sealed class OverrideResolutionException : Exception
    {
        public OverrideResolutionException(string message)
            : base(message)
        {
        }
    }

    public static string? ResolveProjectPath(string workspaceRoot, string overrideTarget)
        => DistillCommandRewriter.ResolveProjectPath(workspaceRoot, overrideTarget);

    public static IReadOnlyList<EvidenceCapability> MergeProducerCapabilities(
        IReadOnlyList<EvidenceCapability> catalog,
        string? profile = null)
        => ProofProducerCapabilities.Merge(catalog, profile);

    public static IReadOnlyList<EvidenceCapability> BuildCatalog(
        string workspaceRoot,
        string? distillConfigPath = null,
        string? profile = null)
        => DistillCapabilitySource.BuildCatalog(workspaceRoot, distillConfigPath, profile);

    public static string ResolveConfigPath(string workspaceRoot, string? configuredPath)
        => DistillCapabilitySource.ResolveConfigPath(workspaceRoot, configuredPath);

    public static string? ComputeArtifactSha256(string? artifactPointer)
        => ArtifactHasher.ComputeSha256Hex(artifactPointer);
}
