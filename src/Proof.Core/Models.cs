using System.Text.Json.Serialization;

namespace Proof.Core;

public sealed record LineSpan(string File, int StartLine, int EndLine);

public enum FileChangeKind
{
    Added,
    Modified,
    Deleted,
    Renamed,
    BinaryModified,
    SubmoduleChanged,
    Unsupported
}

public sealed record FileDelta(
    FileChangeKind Kind,
    string? OldPath,
    string? NewPath,
    IReadOnlyList<LineSpan> OldSpans,
    IReadOnlyList<LineSpan> NewSpans,
    string? OldContentSha256 = null,
    string? NewContentSha256 = null);

public sealed record SourceSnapshot(
    string RepositoryRoot,
    string BaseCommitSha,
    string HeadCommitSha,
    bool IsDirty,
    string SourceDigest,
    IReadOnlyList<FileDelta> Files,
    bool ChangeSetIsEmpty,
    bool UntrackedCaptureFailed = false,
    bool UsedFileWideFallback = false,
    bool ContentHashCaptureFailed = false,
    bool StructuredDeltaFailed = false);

public sealed record ChangedSymbolRef(
    string Id,
    string Project,
    string File,
    string DisplayName,
    int? StartLine,
    int? EndLine,
    bool IsPublic,
    bool IsTest);

public sealed record ImpactedSymbolRef(
    string Id,
    string Project,
    string File,
    string DisplayName,
    string RootChangedSymbolId,
    int Depth,
    bool IsTest,
    string ResolutionKind,
    double? Confidence);

public sealed record ImpactRelation(
    string RootChangedSymbolId,
    string ImpactedSymbolId,
    int Depth,
    string EdgeKind,
    string ResolutionKind,
    double? Confidence);

public sealed record CallerRelation(
    string ChangedSymbolId,
    string CallerSymbolId,
    string CallerProject,
    string? File,
    string ResolutionKind,
    double? Confidence,
    bool IsTest);

public enum CoverageState
{
    Complete,
    Partial,
    Unknown,
    PotentiallyTruncated
}

public sealed record ImpactCompleteness(
    CoverageState Location,
    CoverageState Traversal,
    int MaxDepth,
    int ImpactResultLimit,
    int CallerResultLimit,
    bool ImpactPotentiallyTruncated,
    bool CallerPotentiallyTruncated,
    int UnknownSpanCount,
    int HeuristicRelationCount,
    double? MinimumConfidence);

public enum ObligationKind
{
    Build,
    Test,
    Compatibility,
    CallerContract,
    CrossProject,
    StaticAnalysis,
    TestMapping,
    ManualReview,
    AppContract,
    Architecture,
    Uncertainty
}

public enum ObligationStatus
{
    Pending,
    Proven,
    Failed,
    Unresolved,
    Blocked
}

public enum SubjectKind
{
    Repository,
    Solution,
    Project,
    Assembly,
    File,
    Symbol,
    Test,
    ApiSurface
}

public sealed record ProofSubject(
    SubjectKind Kind,
    string Id,
    string? Project = null,
    string? File = null,
    string? DisplayName = null,
    string? TargetFramework = null);

public sealed record ProofObligation(
    string Id,
    string RuleId,
    ObligationKind Kind,
    string Claim,
    string SubjectId,
    bool Required,
    int RiskWeight,
    IReadOnlyList<string> Reasons,
    ProofSubject? Subject = null);

public enum EvidenceKind
{
    Build,
    TestRun,
    TestCase,
    ApiCompatibility,
    StaticAnalysis,
    TestMapping,
    RuntimeCoverage,
    ManualReview,
    Architecture
}

public enum ArchitecturePolicyMode
{
    Off,
    Advisory,
    Required
}

public enum EvidenceStatus
{
    Pass,
    Fail,
    InfraError,
    Skipped,
    Inconclusive
}

public enum ScopeMode
{
    Exact,
    Contains,
    RepositoryWide
}

public sealed record EvidenceSubjectRef(
    SubjectKind Kind,
    string Id,
    string? Project = null,
    string? File = null,
    string? DisplayName = null,
    string? FullyQualifiedName = null,
    string? TargetFramework = null);

public sealed record EvidenceScope(
    ScopeMode Mode = ScopeMode.Contains,
    IReadOnlyList<string>? Subjects = null,
    string? CommandTarget = null,
    IReadOnlyList<EvidenceSubjectRef>? SubjectRefs = null);

public sealed record EvidenceProvenance(
    string Source,
    string? ArtifactPointer = null,
    string? SourceDigest = null,
    string? CheckId = null,
    string? Sha256 = null);

public sealed record ProofEvidence(
    string Id,
    EvidenceKind Kind,
    string Subject,
    EvidenceStatus Status,
    EvidenceProvenance Provenance,
    EvidenceScope? Scope = null);

public enum ProofVerdict
{
    Proven,
    NotReady,
    Uncertain,
    InfraError,
    NoChange
}

public sealed record ObligationEvidenceLink(
    string ObligationId,
    string EvidenceId,
    string Relation,
    int Strength,
    string? BindingRuleId = null,
    string? ReasonCode = null,
    string? Explanation = null);

public sealed record ChangeRequest(
    string WorkspaceRoot,
    string BaseRevision,
    string HeadRevision,
    IReadOnlyList<LineSpan> Spans,
    string? SourceDigest = null,
    bool ChangeSetIsEmpty = false,
    bool UsedFileWideFallback = false,
    bool UntrackedCaptureFailed = false,
    IReadOnlyList<FileDelta>? FileDeltas = null,
    bool ContentHashCaptureFailed = false,
    bool StructuredDeltaFailed = false,
    bool IsDirty = false);

public sealed record ChangeImpact(
    string BaseRevision,
    string HeadRevision,
    IReadOnlyList<LineSpan> Spans,
    IReadOnlyList<ChangedSymbolRef> ChangedSymbols,
    IReadOnlyList<ImpactedSymbolRef> ImpactedSymbols,
    IReadOnlyList<string> ImpactedProjects,
    IReadOnlyList<string> CallerSubjectIds,
    string CoverageStatus,
    bool HasHeuristicEdges,
    IReadOnlyList<ImpactRelation>? Relations = null,
    IReadOnlyList<CallerRelation>? Callers = null,
    ImpactCompleteness? Completeness = null,
    IReadOnlyList<AnalysisConstraint>? Constraints = null,
    string? SourceDigest = null,
    bool UsedFileWideFallback = false,
    bool UntrackedCaptureFailed = false,
    IReadOnlyList<FileDelta>? FileDeltas = null,
    bool ContentHashCaptureFailed = false,
    bool StructuredDeltaFailed = false,
    IReadOnlyList<string>? DeletionPathsResolved = null,
    IReadOnlyList<ArchitectureViolationRef>? ArchitectureViolations = null,
    bool? ArchitectureRulesPresent = null);

// CodeMap 아키텍처 위반 하나. Proof 쪽으로 옮긴 것이다. SubjectId는
// 위반 Source다(cycle이면 프로젝트 이름, 아니면 심볼 id). Project,
// File, DisplayName은 가능하면 인덱싱된 스냅샷 심볼에서 채운다.
// 검사가 실행되지 않으면 목록은 null이다(정직한 unresolved
// 경로). 실행되어 위반이 없으면 비어 있다.
public sealed record ArchitectureViolationRef(
    string Kind,
    string Message,
    string SubjectId,
    string? Project = null,
    string? File = null,
    string? DisplayName = null);

public sealed record AnalysisConstraint(
    string Id,
    string Code,
    string Severity,
    string Message,
    string? Subject = null);

public sealed record ProofPlan(
    IReadOnlyList<ProofObligation> Obligations,
    IReadOnlyList<AnalysisConstraint>? Constraints = null,
    string? SourceDigest = null,
    bool ChangeSetIsEmpty = false);

public sealed record EvidenceCapability(
    string CheckId,
    string Kind,
    string? CommandTarget,
    ScopeMode ScopeMode,
    IReadOnlyList<string>? DependsOn = null,
    int Cost = 1,
    string? OverrideTarget = null,
    string? TestFilter = null);

public sealed record UncoveredObligation(
    string ObligationId,
    string RuleId,
    string Reason);

public sealed record PlannedVerificationCheck(
    string CheckId,
    string Kind,
    string Command,
    IReadOnlyList<string>? SatisfiesObligationIds = null,
    IReadOnlyList<string>? DependsOn = null,
    string? SelectionReason = null,
    string? OverrideTarget = null,
    string? TestFilter = null);

public sealed record VerificationPlan(
    IReadOnlyList<PlannedVerificationCheck> Checks,
    IReadOnlyList<UncoveredObligation> Uncovered,
    string Profile,
    string? DefinitionDigest = null,
    bool SourceDirty = false);

public sealed record VerificationEvidenceSet(
    IReadOnlyList<ProofEvidence> Evidence,
    IReadOnlyList<ObligationEvidenceLink> Links);

public sealed record VerificationRunResult(
    VerificationEvidenceSet Evidence,
    IReadOnlyList<string> CoverageArtifactPaths);

public sealed record EvaluatedObligation(
    ProofObligation Obligation,
    ObligationStatus Status,
    IReadOnlyList<string> EvidenceIds);

public sealed record ProofEvaluation(
    ProofVerdict Verdict,
    IReadOnlyList<EvaluatedObligation> Obligations,
    string? ReasonCode = null);

// 바이너리 다이제스트는 값이 있을 때만 직렬화한다. null을 쓰면 기존 증명서의
// statement 다이제스트가 바뀐다.
public sealed record ToolchainIdentity(
    string ProofVersion,
    string? CodeMapVersion = null,
    string? DistillVersion = null,
    string? ProofConfigDigest = null,
    string? DistillConfigDigest = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ProofBinarySha256 = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CodeMapBinarySha256 = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DistillBinarySha256 = null);

public sealed record ChangeCertificate(
    int SchemaVersion,
    string BaseRevision,
    string HeadRevision,
    ProofVerdict Verdict,
    ChangeImpact Impact,
    ProofPlan Plan,
    VerificationEvidenceSet Evidence,
    ProofEvaluation Evaluation,
    string? RunId = null,
    string? SourceDigest = null,
    bool IsDirty = false,
    ToolchainIdentity? Toolchain = null,
    VerificationPlan? VerificationPlan = null,
    IReadOnlyList<AnalysisConstraint>? Constraints = null,
    string? CertificateDigest = null,
    string? StatementDigest = null,
    IReadOnlyList<EvidenceArtifactManifestEntry>? ArtifactManifest = null,
    AttestationEnvelope? Attestation = null);

public sealed record SourceIdentity(
    string BaseCommitSha,
    string HeadCommitSha,
    string WorkspaceState,
    string ManifestDigest,
    int FileCount,
    string? HeadTreeSha = null);

public sealed record EvidenceArtifactManifestEntry(
    string LogicalId,
    string RelativePath,
    string Sha256,
    long? Length = null,
    string? MediaType = null);

public sealed record AttestationEnvelope(
    string StatementDigest,
    string? SignerIdentity = null,
    string? Provider = null,
    string? Payload = null,
    IReadOnlyDictionary<string, string>? Claims = null);

// 증명 클레임으로 기록된 CI 신원 출처. 인증서를 그것을 만든
// repository/ref/workflow/commit에 묶는다. `Actor`는
// 의도적으로 없다. 사람 사용자 이름은 신뢰 앵커가 아니다.
public sealed record AttestationContext(
    string RunId,
    string? Repository = null,
    string? Branch = null,
    string? Ref = null,
    string? WorkflowRef = null,
    string? WorkflowSha = null,
    string? CommitSha = null,
    string? RunAttempt = null);

// 검증 신뢰 정책. 서명 유효성과 신뢰는 별개다. 암호학적으로
// 유효한 봉투라도 신원 클레임이 정책과 맞지 않으면 신뢰되지 않는다.
// 각 신원 필드는 선택이다. 설정되지 않은 필드는 강제되지
// 않으므로, 레거시 인증서는 검증 가능한 채로 남는다.
public sealed record TrustPolicy(
    bool RequireSigned = false,
    IReadOnlyList<string>? TrustedIssuers = null,
    string? Repository = null,
    IReadOnlyList<string>? AllowedRefs = null,
    IReadOnlyList<string>? AllowedWorkflows = null,
    string? ExpectedCommitSha = null);

public sealed record AttestationVerificationResult(
    bool SignatureValid,
    bool Trusted,
    string? Reason = null);

public sealed class ProofConfigException : Exception
{
    public ProofConfigException(string message)
        : base(message)
    {
    }

    public ProofConfigException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
