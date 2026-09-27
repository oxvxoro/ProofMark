using Proof.Core;

namespace Proof.Engine;

/// <summary>
/// 명시적 YAML 테스트 맵 생산자. SubjectId 또는 DisplayName이 설정된
/// 맵 항목과 맞는 P005 의무에만 TestMapping 증거를 낸다.
/// 매핑되지 않은 심볼은 모두 정직하게 Unresolved로 남는다. 호출자 관계는
/// 매핑 증명으로 결코 쓰이지 않는다(그것은 CodeMap 생산자의 no-op 계약으로 남는다).
/// </summary>
public sealed class YamlTestMappingEvidenceProducer : ITestMappingEvidenceProducer
{
    private readonly IReadOnlyList<TestMapEntry>? _testMaps;

    public YamlTestMappingEvidenceProducer(IReadOnlyList<TestMapEntry>? testMaps)
    {
        _testMaps = testMaps;
    }

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

        if (_testMaps is not { Count: > 0 } || proofPlan.Obligations.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<ProofEvidence>>([]);
        }

        var evidence = new List<ProofEvidence>();
        var index = 0;
        foreach (var obligation in proofPlan.Obligations.Where(item => item.Kind == ObligationKind.TestMapping))
        {
            if (!_testMaps.Any(entry => TestMapMatching.SymbolMatches(obligation.SubjectId, obligation.Subject?.DisplayName, entry.Symbol)))
            {
                // 이 심볼을 덮는 맵 항목이 없다. 의무는 열린 채로 남는다.
                continue;
            }

            index++;
            evidence.Add(new ProofEvidence(
                $"TM{index}",
                EvidenceKind.TestMapping,
                obligation.SubjectId,
                EvidenceStatus.Pass,
                new EvidenceProvenance(
                    "test-map",
                    CheckId: EvidenceCheckIds.TestMapping,
                    SourceDigest: request.SourceDigest ?? proofPlan.SourceDigest),
                new EvidenceScope(
                    ScopeMode.Exact,
                    CommandTarget: null,
                    SubjectRefs:
                    [
                        new EvidenceSubjectRef(
                            SubjectKind.Symbol,
                            obligation.SubjectId,
                            obligation.Subject?.Project,
                            obligation.Subject?.File,
                            obligation.Subject?.DisplayName)
                    ])));
        }

        return Task.FromResult<IReadOnlyList<ProofEvidence>>(evidence);
    }
}
