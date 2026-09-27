using Proof.Core;

namespace Proof.Adapters.CodeMap;

/// <summary>
/// P011 증거 생산자. 계획 순서대로 Architecture 의무마다 Architecture 증거를
/// 하나씩 낸다.
/// - 주체 <c>architecture-rules</c>(검사가 실행되지 않음 / 규칙 없음) →
///   <see cref="EvidenceStatus.Inconclusive"/>. 빈틈은 계속 보이며,
///   의무는 Unresolved로 끝나고 결코 조용한 Pass가 되지 않는다.
/// - 그 외에 영향이 그 주체의 위반을 여전히 가지면 <see cref="EvidenceStatus.Fail"/>,
///   검사가 실행되었고 주체가 깨끗하면 <see cref="EvidenceStatus.Pass"/>다.
/// 증거는 <c>architecture</c> 능력 CheckId와
/// 정확한 주체 참조를 통해서만 묶인다.
/// 다른 검사는 P011을 닫을 수 없다.
/// </summary>
public sealed class ArchitectureEvidenceProducer : IArchitectureEvidenceProducer
{
    // 생산자 능력 CheckId의 유일한 소유자다.
    // DistillVerificationRunner.MergeProducerCapabilities와 정확히 일치해야 한다.
    // 아니면 바인더의 allowedChecks 게이트가 모든 P011 연결을 조용히 거부한다.
    public const string CheckId = EvidenceCheckIds.Architecture;

    public Task<IReadOnlyList<ProofEvidence>> AnalyzeAsync(
        ChangeRequest request,
        ChangeImpact impact,
        ProofPlan proofPlan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(impact);
        ArgumentNullException.ThrowIfNull(proofPlan);
        cancellationToken.ThrowIfCancellationRequested();

        var obligations = proofPlan.Obligations.Where(item => item.Kind == ObligationKind.Architecture).ToArray();
        if (obligations.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<ProofEvidence>>([]);
        }

        var digest = request.SourceDigest ?? proofPlan.SourceDigest;
        var evidence = new List<ProofEvidence>();
        var index = 0;
        foreach (var obligation in obligations)
        {
            index++;
            var status = obligation.SubjectId == "architecture-rules"
                ? EvidenceStatus.Inconclusive
                : (impact.ArchitectureViolations ?? []).Any(item =>
                        string.Equals(item.SubjectId, obligation.SubjectId, StringComparison.Ordinal))
                    ? EvidenceStatus.Fail
                    : EvidenceStatus.Pass;

            evidence.Add(new ProofEvidence(
                $"ARCH{index}",
                EvidenceKind.Architecture,
                obligation.SubjectId,
                status,
                new EvidenceProvenance(
                    "architecture",
                    CheckId: CheckId,
                    SourceDigest: digest),
                new EvidenceScope(
                    ScopeMode.Exact,
                    Subjects: obligation.SubjectId == "architecture-rules"
                        ? null
                        : (impact.ArchitectureViolations ?? [])
                            .Where(item => string.Equals(item.SubjectId, obligation.SubjectId, StringComparison.Ordinal))
                            .Select(item => item.Message)
                            .Distinct(StringComparer.Ordinal)
                            .ToArray(),
                    CommandTarget: null,
                    SubjectRefs:
                    [
                        new EvidenceSubjectRef(
                            obligation.Subject?.Kind ?? SubjectKind.Symbol,
                            obligation.SubjectId,
                            obligation.Subject?.Project,
                            obligation.Subject?.File,
                            obligation.Subject?.DisplayName)
                    ])));
        }

        return Task.FromResult<IReadOnlyList<ProofEvidence>>(evidence);
    }
}
