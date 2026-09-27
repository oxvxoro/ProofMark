using Proof.Core;

namespace Proof.Adapters.CodeMap;

/// <summary>
/// 테스트 매핑 생산자. P005는 변경된 심볼의 매핑된 테스트를 찾는 의무다.
/// CodeMap 호출자 관계는 매핑이 존재함을 결코 증명할 수 없으므로,
/// 이 생산자는 의도적으로 증거를 내지 않는다. 의무는 실제 매핑 원천
/// (명시적 맵, 런타임 커버리지)이 주체와 맞는 증거를 줄 때까지
/// Unresolved로 남는다. 테스트 호출자는 이미 플래너가
/// CallerRelation.IsTest를 통해 P004 의무로 드러낸다.
/// </summary>
public sealed class CodeMapTestMappingEvidenceProducer : ITestMappingEvidenceProducer
{
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

        // 호출자 관계는 P005 구멍의 부재를 표시할 뿐, 그 증명은 결코 아니다.
        return Task.FromResult<IReadOnlyList<ProofEvidence>>([]);
    }
}
