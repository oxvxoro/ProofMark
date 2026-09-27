using Proof.Core;

namespace Proof.Engine;

/// <summary>
/// 생산자가 검증 실행에 대해 언제 돌 수 있는지. 검증 전
/// 생산자는 요청/영향/계획만 필요하고 (긴) Distill
/// 실행과 겹칠 수 있다. 검증 후 생산자는 실행 산출물이 필요하다.
/// </summary>
public enum EvidenceProductionStage
{
    BeforeVerification,
    AfterVerification
}

public interface IEvidenceProducer
{
    string Name { get; }

    int Order { get; }

    EvidenceProductionStage Stage { get; }

    ValueTask<IReadOnlyList<ProofEvidence>> ProduceAsync(
        EvidenceProductionContext context,
        CancellationToken cancellationToken);
}

public sealed record EvidenceProductionContext(
    ChangeRequest Request,
    ChangeImpact Impact,
    ProofPlan ProofPlan,
    VerificationPlan? VerificationPlan,
    VerificationRunResult? VerificationRun,
    IReadOnlyList<ApiCompatibilityFact>? ApiCompatibilityFacts = null);

internal sealed class ApiCompatibilityEvidenceAdapter(IApiCompatibilityEvidenceProducer producer) : IEvidenceProducer
{
    public string Name => "api-compatibility";

    public int Order => 0;

    public EvidenceProductionStage Stage => EvidenceProductionStage.BeforeVerification;

    public async ValueTask<IReadOnlyList<ProofEvidence>> ProduceAsync(
        EvidenceProductionContext context,
        CancellationToken cancellationToken)
        => await producer.AnalyzeAsync(
            context.Request,
            context.ProofPlan,
            context.ApiCompatibilityFacts ?? [],
            cancellationToken).ConfigureAwait(false);
}

internal sealed class TestMappingEvidenceAdapter(ITestMappingEvidenceProducer producer) : IEvidenceProducer
{
    public string Name => "test-mapping";

    public int Order => 1;

    public EvidenceProductionStage Stage => EvidenceProductionStage.BeforeVerification;

    public async ValueTask<IReadOnlyList<ProofEvidence>> ProduceAsync(
        EvidenceProductionContext context,
        CancellationToken cancellationToken)
        => await producer.AnalyzeAsync(context.Request, context.Impact, context.ProofPlan, cancellationToken).ConfigureAwait(false);
}

internal sealed class RuntimeCoverageEvidenceAdapter(IRuntimeCoverageEvidenceProducer producer) : IEvidenceProducer
{
    public string Name => "runtime-coverage";

    public int Order => 2;

    public EvidenceProductionStage Stage => EvidenceProductionStage.AfterVerification;

    public async ValueTask<IReadOnlyList<ProofEvidence>> ProduceAsync(
        EvidenceProductionContext context,
        CancellationToken cancellationToken)
        => await producer.AnalyzeAsync(
            context.Request,
            context.Impact,
            context.ProofPlan,
            context.VerificationRun?.CoverageArtifactPaths ?? [],
            cancellationToken).ConfigureAwait(false);
}

internal sealed class ManualReviewEvidenceAdapter(IManualReviewEvidenceProducer producer) : IEvidenceProducer
{
    public string Name => "manual-review";

    public int Order => 3;

    public EvidenceProductionStage Stage => EvidenceProductionStage.BeforeVerification;

    public async ValueTask<IReadOnlyList<ProofEvidence>> ProduceAsync(
        EvidenceProductionContext context,
        CancellationToken cancellationToken)
        => await producer.AnalyzeAsync(context.Request, context.ProofPlan, cancellationToken).ConfigureAwait(false);
}

internal sealed class ArchitectureEvidenceAdapter(IArchitectureEvidenceProducer producer) : IEvidenceProducer
{
    public string Name => "architecture";

    public int Order => 4;

    public EvidenceProductionStage Stage => EvidenceProductionStage.BeforeVerification;

    public async ValueTask<IReadOnlyList<ProofEvidence>> ProduceAsync(
        EvidenceProductionContext context,
        CancellationToken cancellationToken)
        => await producer.AnalyzeAsync(context.Request, context.Impact, context.ProofPlan, cancellationToken).ConfigureAwait(false);
}
