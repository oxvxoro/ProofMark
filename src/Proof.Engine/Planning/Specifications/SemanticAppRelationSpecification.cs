using System.Collections.Frozen;
using Proof.Core;

namespace Proof.Engine.Planning.Specifications;

/// <summary>
/// 신뢰 하한보다 높은 의미적 앱 그래프 간선인 영향 관계와 맞는다.
/// 휴리스틱이거나 신뢰가 낮은 간선은 앱 계약이 결코 아니다.
/// </summary>
internal sealed class SemanticAppRelationSpecification : IProofSpecification<ImpactRelation>
{
    private readonly FrozenSet<string> _appEdgeKinds;
    private readonly double _minConfidence;

    public SemanticAppRelationSpecification(ProofPolicy policy)
    {
        _appEdgeKinds = AppContractObligationRule.AppEdgeKinds.ToFrozenSet(StringComparer.Ordinal);
        _minConfidence = policy.MinConfidence is > 0 ? policy.MinConfidence : 0.75;
    }

    public SpecificationResult Evaluate(ImpactRelation candidate)
    {
        if (!_appEdgeKinds.Contains(candidate.EdgeKind))
        {
            return SpecificationResult.NotSatisfied(explanation: "edge kind is not an app-contract relation");
        }

        if (string.Equals(candidate.ResolutionKind, "Heuristic", StringComparison.OrdinalIgnoreCase)
            || (candidate.Confidence is { } confidence && confidence < _minConfidence))
        {
            return SpecificationResult.NotSatisfied(
                explanation: "app relation is heuristic or below the confidence floor");
        }

        return SpecificationResult.Satisfied;
    }
}
