using Proof.Core;

namespace Proof.Engine.Planning;

/// <summary>
/// <see cref="ChangeImpact"/> 위의 한 번 통과 조회 테이블. 의무 규칙이
/// 심볼마다 변경/영향 심볼과 호출자 관계를 다시 훑지 않게 한다.
/// 구성은 각 그룹 안에서 원본 순서를 유지하고, 중복 id는 첫 항목만
/// 남긴다. 이전 <c>FirstOrDefault</c> 의미와 같다.
/// </summary>
internal sealed class PlanningIndex
{
    public PlanningIndex(
        ChangeImpact impact,
        IReadOnlyList<CallerRelation> callers,
        IReadOnlyList<ImpactRelation> relations)
    {
        var changedById = new Dictionary<string, ChangedSymbolRef>(StringComparer.Ordinal);
        foreach (var symbol in impact.ChangedSymbols)
        {
            changedById.TryAdd(symbol.Id, symbol);
        }

        var impactedById = new Dictionary<string, ImpactedSymbolRef>(StringComparer.Ordinal);
        foreach (var symbol in impact.ImpactedSymbols)
        {
            impactedById.TryAdd(symbol.Id, symbol);
        }

        ChangedById = changedById;
        ImpactedById = impactedById;
        CallersByChangedId = Group(callers, static caller => caller.ChangedSymbolId);
        TestCallersByChangedId = Group(
            callers.Where(static caller => caller.IsTest),
            static caller => caller.ChangedSymbolId);

        ChangedProjects = impact.ChangedSymbols
            .Select(static symbol => symbol.Project)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        TestImpactRoots = ComputeTestImpactRoots(impact, relations, impactedById);
    }

    public IReadOnlyDictionary<string, ChangedSymbolRef> ChangedById { get; }

    public IReadOnlyDictionary<string, ImpactedSymbolRef> ImpactedById { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<CallerRelation>> CallersByChangedId { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<CallerRelation>> TestCallersByChangedId { get; }

    public IReadOnlySet<string> TestImpactRoots { get; }

    public IReadOnlySet<string> ChangedProjects { get; }

    private static IReadOnlyDictionary<string, IReadOnlyList<CallerRelation>> Group(
        IEnumerable<CallerRelation> callers,
        Func<CallerRelation, string> keySelector)
    {
        var groups = new Dictionary<string, List<CallerRelation>>(StringComparer.Ordinal);
        foreach (var caller in callers)
        {
            if (!groups.TryGetValue(keySelector(caller), out var list))
            {
                list = [];
                groups[keySelector(caller)] = list;
            }

            list.Add(caller);
        }

        return groups.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<CallerRelation>)pair.Value,
            StringComparer.Ordinal);
    }

    private static IReadOnlySet<string> ComputeTestImpactRoots(
        ChangeImpact impact,
        IReadOnlyList<ImpactRelation> relations,
        IReadOnlyDictionary<string, ImpactedSymbolRef> impactedById)
    {
        var roots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relation in relations)
        {
            if (impactedById.TryGetValue(relation.ImpactedSymbolId, out var impacted) && impacted.IsTest)
            {
                roots.Add(relation.RootChangedSymbolId);
            }
        }

        // Test 규칙은 관계 목록에 없어도 영향받은 테스트 심볼을
        // 커버리지로 본다.
        foreach (var impacted in impact.ImpactedSymbols)
        {
            if (impacted.IsTest)
            {
                roots.Add(impacted.RootChangedSymbolId);
            }
        }

        return roots;
    }
}
