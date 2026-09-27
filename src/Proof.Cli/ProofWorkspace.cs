using Proof.Adapters.Distill;
using Proof.Adapters.Git;
using Proof.Core;
using Proof.Engine;

namespace Proof.Cli;

/// <summary>
/// 계획하거나 검증하는 모든 명령이 쓰는 공유 워크스페이스 기동.
/// 현재 디렉터리 → <see cref="ProofConfig.Load"/> → git base/head
/// 해석 → 스냅샷 수집 → <see cref="ProofRuntimeFactory.Create"/>
/// → 오케스트레이터 계획. Proof.Mcp(InternalsVisibleTo)가 같은
/// 경로를 재사용해 MCP 도구가 CLI와 어긋나지 않게 한다.
/// </summary>
internal static class ProofWorkspace
{
    public static string CurrentRoot() => Directory.GetCurrentDirectory();

    public static ProofConfig LoadConfig() => ProofConfig.Load(CurrentRoot());

    public static async Task<(string EffectiveBase, string HeadSha)> ResolveBaseAsync(
        string workspaceRoot,
        ProofConfig config,
        string? baseRevision,
        CancellationToken cancellationToken)
    {
        var effectiveBase = await GitBaseResolver.ResolveAsync(
                workspaceRoot,
                baseRevision ?? config.Proof.Base.Ref,
                config.Proof.Base.Strategy,
                cancellationToken)
            .ConfigureAwait(false);
        var headSha = await GitBaseResolver.ResolveHeadAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        return (effectiveBase, headSha);
    }

    public static async Task<SourceSnapshot> CollectSnapshotAsync(
        string workspaceRoot,
        string effectiveBase,
        string headSha,
        CancellationToken cancellationToken)
        => await new GitSourceSnapshotCollector()
            .CollectAsync(workspaceRoot, effectiveBase, headSha, cancellationToken)
            .ConfigureAwait(false);

    public static ProofRuntimeContext CreateRuntime(
        string workspaceRoot,
        ProofConfig config,
        IProofMetricsSink? metricsSink = null)
        => ProofRuntimeFactory.Create(workspaceRoot, config, metricsSink);

    /// <summary>계획만 필요한 명령(plan / map suggest / from-coverage)용 전체 파이프라인.</summary>
    public static async Task<PlannedWorkspace> PlanAsync(
        string? baseRevision,
        string? profile,
        CancellationToken cancellationToken)
    {
        var workspaceRoot = CurrentRoot();
        var config = LoadConfig();
        var (effectiveBase, headSha) = await ResolveBaseAsync(workspaceRoot, config, baseRevision, cancellationToken).ConfigureAwait(false);
        var snapshot = await CollectSnapshotAsync(workspaceRoot, effectiveBase, headSha, cancellationToken).ConfigureAwait(false);
        var runtime = CreateRuntime(workspaceRoot, config);
        var (impact, plan, verificationPlan) = await PlanWithRuntimeAsync(
            runtime, config, snapshot, profile, cancellationToken).ConfigureAwait(false);
        return new PlannedWorkspace(workspaceRoot, config, runtime.DistillConfigPath, snapshot, impact, plan, verificationPlan);
    }

    public static async Task<(ChangeImpact Impact, ProofPlan Plan, VerificationPlan? VerificationPlan)> PlanWithRuntimeAsync(
        ProofRuntimeContext runtime,
        ProofConfig config,
        SourceSnapshot snapshot,
        string? profile,
        CancellationToken cancellationToken)
    {
        var effectiveProfile = profile ?? config.Verification.DistillProfile;
        var (_, impact, plan, verificationPlan) = await runtime.Orchestrator.PlanAsync(
            snapshot,
            effectiveProfile,
            cancellationToken,
            config.ToPolicy(),
            DistillVerificationRunner.MergeProducerCapabilities(
                DistillVerificationRunner.BuildCatalog(runtime.WorkspaceRoot, runtime.DistillConfigPath, effectiveProfile),
                effectiveProfile)).ConfigureAwait(false);
        return (impact, plan, verificationPlan);
    }
}

internal sealed record PlannedWorkspace(
    string WorkspaceRoot,
    ProofConfig Config,
    string DistillConfigPath,
    SourceSnapshot Snapshot,
    ChangeImpact Impact,
    ProofPlan Plan,
    VerificationPlan? VerificationPlan);
