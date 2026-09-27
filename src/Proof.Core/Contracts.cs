namespace Proof.Core;

public interface ISourceSnapshotCollector
{
    Task<SourceSnapshot> CollectAsync(
        string workspaceRoot,
        string baseRevision,
        string headRevision,
        CancellationToken cancellationToken);
}

/// <summary>
/// 엔진 밖에서 증명 출처(repository, branch)를 해석하는 포트.
/// 구현은 어댑터에 있다(예: Proof.Adapters.Git). 엔진은 스스로
/// git을 띄우거나 CI 환경 변수를 결코 읽지 않는다.
/// </summary>
public interface IAttestationContextResolver
{
    Task<AttestationContext> ResolveAsync(
        string workspaceRoot,
        string runId,
        CancellationToken cancellationToken);
}

public interface IChangeImpactProvider
{
    Task<ChangeImpact> AnalyzeAsync(ChangeRequest request, CancellationToken cancellationToken);
}

public interface IChangeAnalysisProvider
{
    Task<ChangeAnalysisResult> AnalyzeChangeAsync(ChangeRequest request, CancellationToken cancellationToken);
}

public interface IProofPlanner
{
    ProofPlan Plan(ChangeImpact impact, ProofPolicy? policy = null);
}

public interface IVerificationRunner
{
    Task<VerificationRunResult> VerifyAsync(
        ProofPlan plan,
        VerificationPlan verificationPlan,
        string workspaceRoot,
        CancellationToken cancellationToken);
}

public interface IVerificationExecutor
{
    Task<IReadOnlyList<ProofEvidence>> ExecuteAsync(
        ProofPlan proofPlan,
        VerificationPlan verificationPlan,
        string workspaceRoot,
        string? distillConfigPath,
        CancellationToken cancellationToken);
}

public interface IEvidenceBinder
{
    VerificationEvidenceSet Bind(
        ProofPlan plan,
        IReadOnlyList<ProofEvidence> evidence,
        VerificationPlan? verificationPlan = null);
}

public interface IVerificationPlanner
{
    VerificationPlan Plan(
        ProofPlan proofPlan,
        IReadOnlyList<EvidenceCapability> catalog,
        string profile);
}

public interface IProofEvaluator
{
    ProofEvaluation Evaluate(ProofPlan plan, VerificationEvidenceSet evidence);
}

public interface IChangeCertificateBuilder
{
    ChangeCertificate Build(
        ChangeImpact impact,
        ProofPlan plan,
        VerificationEvidenceSet evidence,
        ProofEvaluation evaluation,
        CertificateMetadata? metadata = null);
}

public interface IApiCompatibilityEvidenceProducer
{
    Task<IReadOnlyList<ProofEvidence>> AnalyzeAsync(
        ChangeRequest request,
        ProofPlan proofPlan,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ProofEvidence>> AnalyzeAsync(
        ChangeRequest request,
        ProofPlan proofPlan,
        IReadOnlyList<ApiCompatibilityFact> facts,
        CancellationToken cancellationToken);
}

public interface ITestMappingEvidenceProducer
{
    Task<IReadOnlyList<ProofEvidence>> AnalyzeAsync(
        ChangeRequest request,
        ChangeImpact impact,
        ProofPlan proofPlan,
        CancellationToken cancellationToken);
}

// 커버리지 증거는 Distill 테스트 실행 뒤에 만들어진다. 정적
// 테스트 맵 생산자와 달리, 실행이 낸 커버리지 산출물 경로가 필요하다.
// 경로는 별도 접근자로 이동하므로 커버리지가 테스트 실행
// 산출물로 결코 둔갑하지 않는다.
public interface IRuntimeCoverageEvidenceProducer
{
    Task<IReadOnlyList<ProofEvidence>> AnalyzeAsync(
        ChangeRequest request,
        ChangeImpact impact,
        ProofPlan proofPlan,
        IReadOnlyList<string> coverageArtifactPaths,
        CancellationToken cancellationToken);
}

// 수동 리뷰 생산자. 암호학적으로 검증된 리뷰 페이로드가 뒷받침하는
// P009 의무에만 ManualReview 증거를 낸다. 자유 형식 리뷰 텍스트는
// 증명으로 결코 받아들여지지 않는다.
public interface IManualReviewEvidenceProducer
{
    Task<IReadOnlyList<ProofEvidence>> AnalyzeAsync(
        ChangeRequest request,
        ProofPlan proofPlan,
        CancellationToken cancellationToken);
}

// 아키텍처(P011) 생산자. Proof.Adapters.CodeMap에 있다. 위반은
// 영향 제공자가 연 것과 같은 인덱싱된 그래프로 계산되었으므로,
// 입력은 두 번째 인덱스가 아니라 영향 페이로드다.
public interface IArchitectureEvidenceProducer
{
    Task<IReadOnlyList<ProofEvidence>> AnalyzeAsync(
        ChangeRequest request,
        ChangeImpact impact,
        ProofPlan proofPlan,
        CancellationToken cancellationToken);
}

public interface IAttestationSigner
{
    Task<AttestationEnvelope> SignAsync(
        string statementDigest,
        AttestationContext context,
        CancellationToken cancellationToken);
}

public interface IAttestationVerifier
{
    Task<AttestationVerificationResult> VerifyAsync(
        AttestationEnvelope envelope,
        TrustPolicy policy,
        CancellationToken cancellationToken);
}

public sealed record CertificateMetadata(
    string? RunId = null,
    string? ProofConfigDigest = null,
    string? DistillConfigDigest = null,
    bool IsDirty = false,
    VerificationPlan? VerificationPlan = null,
    string? CodeMapVersion = null,
    string? DistillVersion = null,
    string? WorkspaceRoot = null,
    string? ProofBinarySha256 = null,
    string? CodeMapBinarySha256 = null,
    string? DistillBinarySha256 = null);
