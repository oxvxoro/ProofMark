using Proof.Core;

namespace Proof.Engine.Planning;

internal sealed class PlanningContext
{
    public required ChangeImpact Impact { get; init; }

    public required ProofPolicy Policy { get; init; }

    /// <summary>호출자 id 폴백이 이미 적용된 호출자 관계.</summary>
    public required IReadOnlyList<CallerRelation> Callers { get; init; }

    /// <summary>영향받은 심볼 폴백이 이미 적용된 영향 관계.</summary>
    public required IReadOnlyList<ImpactRelation> Relations { get; init; }

    public required PlanningIndex Index { get; init; }

    public List<ProofObligation> Obligations { get; } = [];

    public List<AnalysisConstraint> Constraints { get; init; } = [];
}
