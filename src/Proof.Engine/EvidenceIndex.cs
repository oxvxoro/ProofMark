using Proof.Core;
using Proof.Engine.Binding;

namespace Proof.Engine;

internal enum SourceBindingState
{
    Valid,
    MissingDigest,
    StaleDigest
}

internal sealed record IndexedEvidence(int Ordinal, ProofEvidence Evidence, SourceBindingState State);

/// <summary>
/// 바인더를 위해 인정된 증거를 인덱싱한다. 유효한 증거는 종류별로
/// 담긴다. 의무는 실제로 맞을 수 있는 증거 종류만 순회한다.
/// 오래되었거나 다이제스트가 없는 거부는 종류와 무관하게 남아
/// 원래 순서로 다시 합쳐진다. 서수는 <see cref="CandidatesFor"/>가
/// 이전 전체 스캔이 낸 순서를 정확히 반환하게 한다.
/// </summary>
internal sealed class EvidenceIndex
{
    private readonly IReadOnlyList<IndexedEvidence> _missingDigest;
    private readonly IReadOnlyList<IndexedEvidence> _staleDigest;
    private readonly Dictionary<EvidenceKind, IReadOnlyList<IndexedEvidence>> _validByKind;

    public EvidenceIndex(IReadOnlyList<ProofEvidence> admitted, string? sourceDigest)
    {
        var missing = new List<IndexedEvidence>();
        var stale = new List<IndexedEvidence>();
        var byKind = new Dictionary<EvidenceKind, List<IndexedEvidence>>();
        var ordinal = 0;

        foreach (var item in admitted)
        {
            var state = Classify(item, sourceDigest);
            var indexed = new IndexedEvidence(ordinal++, item, state);
            switch (state)
            {
                case SourceBindingState.MissingDigest:
                    missing.Add(indexed);
                    break;
                case SourceBindingState.StaleDigest:
                    stale.Add(indexed);
                    break;
                default:
                    if (!byKind.TryGetValue(item.Kind, out var list))
                    {
                        list = [];
                        byKind[item.Kind] = list;
                    }

                    list.Add(indexed);
                    break;
            }
        }

        _missingDigest = missing;
        _staleDigest = stale;
        _validByKind = byKind.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<IndexedEvidence>)pair.Value);
    }

    /// <summary>
    /// 의무가 볼 수 있는 증거. 원래 인정된 순서다. 오래되었거나
    /// 다이제스트가 없는 증거는 모든 의무에 포함된다. 이전 바인더가
    /// 종류 검사보다 먼저 거부 링크를 냈기 때문이다.
    /// </summary>
    public IReadOnlyList<IndexedEvidence> CandidatesFor(ProofObligation obligation)
    {
        var kinds = CandidateKindsFor(obligation.Kind);

        // 빠른 경로. 후보 종류가 하나이고 오래됨/누락 거부가 없으면
        // 종류 버킷은 이미 예전 전체 스캔과 같은 순서다.
        if (kinds.Length == 1
            && _missingDigest.Count == 0
            && _staleDigest.Count == 0
            && _validByKind.TryGetValue(kinds[0], out var single))
        {
            return single;
        }

        var total = _missingDigest.Count + _staleDigest.Count;
        foreach (var kind in kinds)
        {
            if (_validByKind.TryGetValue(kind, out var list))
            {
                total += list.Count;
            }
        }

        if (total == 0)
        {
            return [];
        }

        var result = new List<IndexedEvidence>(total);
        result.AddRange(_missingDigest);
        result.AddRange(_staleDigest);
        foreach (var kind in kinds)
        {
            if (_validByKind.TryGetValue(kind, out var list))
            {
                result.AddRange(list);
            }
        }

        result.Sort(static (left, right) => left.Ordinal.CompareTo(right.Ordinal));
        return result;
    }

    // 각 바인딩 규칙이 받아들일 수 있는 증거 종류를 그대로 반영해야 한다.
    // src/Proof.Engine/Binding/*BindingRule.cs를 본다. 동등성 테스트가 고정한다.
    private static EvidenceKind[] CandidateKindsFor(ObligationKind kind) => kind switch
    {
        ObligationKind.Build => [EvidenceKind.Build],
        ObligationKind.CrossProject => [EvidenceKind.Build],
        ObligationKind.Compatibility => [EvidenceKind.ApiCompatibility, EvidenceKind.Build],
        ObligationKind.Test => [EvidenceKind.TestCase, EvidenceKind.TestRun],
        ObligationKind.CallerContract => [EvidenceKind.TestCase, EvidenceKind.TestRun, EvidenceKind.Build],
        ObligationKind.TestMapping => [EvidenceKind.TestMapping, EvidenceKind.RuntimeCoverage],
        ObligationKind.ManualReview => [EvidenceKind.ManualReview],
        ObligationKind.AppContract => [EvidenceKind.RuntimeCoverage, EvidenceKind.Build, EvidenceKind.StaticAnalysis],
        ObligationKind.Architecture => [EvidenceKind.Architecture],
        ObligationKind.StaticAnalysis => [EvidenceKind.StaticAnalysis],
        _ => []
    };

    private static SourceBindingState Classify(ProofEvidence evidence, string? sourceDigest)
    {
        if (!BindingIdentity.IsSourceBound(evidence.Kind))
        {
            return SourceBindingState.Valid;
        }

        if (string.IsNullOrWhiteSpace(sourceDigest))
        {
            return SourceBindingState.Valid;
        }

        if (string.IsNullOrWhiteSpace(evidence.Provenance.SourceDigest))
        {
            return SourceBindingState.MissingDigest;
        }

        return string.Equals(evidence.Provenance.SourceDigest, sourceDigest, StringComparison.Ordinal)
            ? SourceBindingState.Valid
            : SourceBindingState.StaleDigest;
    }
}
