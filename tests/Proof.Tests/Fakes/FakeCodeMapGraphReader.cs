using CodeMap.Core.Contracts;
using CodeMap.Core.Models;

namespace Proof.Tests.Fakes;

/// <summary>
/// 절단 테스트용 최소 <see cref="ICodeMapGraphReader"/> 대역. 영향 순회에
/// 필요한 멤버만 구현되고, 나머지는 예외를 던져 더 많은 그래프 동작에
/// 실수로 의존하는 테스트가 크게 실패하게 한다.
/// </summary>
internal sealed class FakeCodeMapGraphReader : ICodeMapGraphReader
{
    private readonly Dictionary<string, IndexedSymbol> _symbols;
    private readonly Func<IndexedSymbol, int, int, RelationPage<IndexedRelation>> _callers;
    private readonly Func<IndexedSymbol, int, int, RelationPage<ImpactItem>> _impact;

    public FakeCodeMapGraphReader(
        IEnumerable<IndexedSymbol>? symbols = null,
        Func<IndexedSymbol, int, int, RelationPage<IndexedRelation>>? callers = null,
        Func<IndexedSymbol, int, int, RelationPage<ImpactItem>>? impact = null)
    {
        _symbols = (symbols ?? []).ToDictionary(symbol => symbol.Id, StringComparer.Ordinal);
        _callers = callers ?? ((_, _, _) => new RelationPage<IndexedRelation>([], false));
        _impact = impact ?? ((_, _, _) => new RelationPage<ImpactItem>([], false));
    }

    public RelationPage<IndexedRelation> CallerRelationsPaged(
        IndexedSymbol symbol,
        int limit,
        int offset,
        double minConfidence = 0)
        => _callers(symbol, limit, offset);

    public IReadOnlyList<IndexedSymbol> SymbolsInFiles(
        IReadOnlyCollection<string> relativePaths,
        int maxResults = 500)
    {
        var paths = relativePaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _symbols.Values
            .Where(symbol => symbol.Kind is not (NodeKind.File or NodeKind.Namespace)
                && (paths.Contains(symbol.RelativePath)
                    || paths.Contains(symbol.Project + "/" + symbol.RelativePath)))
            .OrderBy(symbol => symbol.QualifiedName, StringComparer.Ordinal)
            .ThenBy(symbol => symbol.Id, StringComparer.Ordinal)
            .Take(maxResults)
            .ToArray();
    }

    public IndexedSymbol? FindById(string id) => _symbols.GetValueOrDefault(id);

    public IReadOnlyDictionary<string, IndexedSymbol> FindByIds(IEnumerable<string> ids)
    {
        var result = new Dictionary<string, IndexedSymbol>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (_symbols.TryGetValue(id, out var symbol))
            {
                result[id] = symbol;
            }
        }

        return result;
    }

    public IReadOnlyList<IndexedSymbol> Find(string query, int maxResults = 20) =>
        throw new NotSupportedException();

    public SymbolSearchResult ResolveSymbol(string query, bool callableOnly, int maxResults) =>
        throw new NotSupportedException();

    public IReadOnlyList<IndexedFile> Files() => throw new NotSupportedException();

    public IReadOnlyDictionary<string, IndexedFile> FindFilesByIds(IEnumerable<string> ids) =>
        throw new NotSupportedException();

    public IReadOnlyList<IndexedSymbol> Members(IndexedSymbol symbol, int maxResults = 200) =>
        throw new NotSupportedException();

    public IReadOnlyList<IndexedRelation> CallerRelations(IndexedSymbol symbol, int maxResults = 200) =>
        throw new NotSupportedException();

    public IReadOnlyList<IndexedRelation> CalleeRelations(IndexedSymbol symbol, int depth = 1, int maxResults = 200) =>
        throw new NotSupportedException();

    public IReadOnlyList<IndexedRelation> ImplementationRelations(IndexedSymbol symbol, int maxResults = 200) =>
        throw new NotSupportedException();

    public IReadOnlyList<IndexedRelation> ReferencedByRelations(IndexedSymbol symbol, int maxResults = 200) =>
        throw new NotSupportedException();

    public RelationPage<IndexedRelation> CalleeRelationsPaged(
        IndexedSymbol symbol,
        int depth,
        int limit,
        int offset,
        double minConfidence = 0) => throw new NotSupportedException();

    public RelationPage<IndexedRelation> ImplementationRelationsPaged(
        IndexedSymbol symbol,
        int limit,
        int offset,
        double minConfidence = 0) => throw new NotSupportedException();

    public RelationPage<ImpactItem> ImpactPaged(
        IndexedSymbol root,
        int depth,
        int limit,
        int offset,
        string profile) => _impact(root, limit, offset);

    public RelationPage<ImpactItem> FlowPaged(
        IndexedSymbol entry,
        string kind,
        int depth,
        int limit,
        int offset,
        double minConfidence) => throw new NotSupportedException();

    public RelationPage<IndexedSymbol> MembersPaged(IndexedSymbol symbol, int limit, int offset) =>
        throw new NotSupportedException();

    public IReadOnlyList<RelationQueryResult> Relations(
        string sourceId,
        string targetId,
        EdgeKind? edgeKind,
        int maxResults,
        double minConfidence) => throw new NotSupportedException();

    public IReadOnlyList<ImpactItem> Impact(IndexedSymbol root, int depth, int maxResults, string profile) =>
        throw new NotSupportedException();

    public IReadOnlyList<ImpactItem> ImpactUnion(
        IEnumerable<IndexedSymbol> roots,
        int depth,
        int maxResults,
        string profile) => throw new NotSupportedException();

    public IReadOnlyList<RootedImpactItem> ImpactUnionDetailed(
        IReadOnlyList<IndexedSymbol> roots,
        int depth,
        int maxResults,
        string profile) => throw new NotSupportedException();

    public IReadOnlyList<IndexedSymbol> SymbolsIntersecting(
        string relativePath,
        int startLine,
        int endLine,
        int maxResults = 500) => throw new NotSupportedException();

    public IReadOnlyList<IndexedSymbol> PublicSymbols(int maxResults = 100000) =>
        _symbols.Values
            .Where(symbol => string.Equals(symbol.Visibility, "public", StringComparison.OrdinalIgnoreCase)
                             && symbol.Kind is not (NodeKind.File or NodeKind.Namespace))
            .OrderBy(symbol => symbol.Project, StringComparer.Ordinal)
            .ThenBy(symbol => symbol.QualifiedName, StringComparer.Ordinal)
            .ThenBy(symbol => symbol.Id, StringComparer.Ordinal)
            .Take(maxResults)
            .ToArray();

    public IReadOnlyList<ImpactItem> Flow(
        IndexedSymbol entry,
        string kind,
        int depth,
        int maxResults,
        double minConfidence) => throw new NotSupportedException();

    public RepoMap BuildMap(string? focus, string? project, int tokenBudget) =>
        throw new NotSupportedException();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
