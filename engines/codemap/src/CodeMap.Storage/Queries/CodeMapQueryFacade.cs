using CodeMap.Storage;
using CodeMap.Core.Models;

namespace CodeMap.Storage.Queries;

/// <summary>
/// 기능 중심 질의 조합 지점. 어댑터가 이 전용 서비스로 옮기는 동안
/// CodeMapQueryService는 호환 파사드로 남는다.
/// </summary>
public sealed class CodeMapQueryFacade(CodeMapQueryService service)
{
    public SymbolSearchService Symbols { get; } = new(service);
    public RelationQueryService Relations { get; } = new(service);
    public ImpactQueryService Impact { get; } = new(service);
    public FlowQueryService Flow { get; } = new(service);
    public RepositoryMapService Map { get; } = new(service);
    public QueryEvidenceService Evidence { get; } = new();
}

public sealed class SymbolSearchService(CodeMapQueryService service)
{
    public IReadOnlyList<IndexedSymbol> Find(string query, int maxResults = QueryLimits.DefaultMaxResults) => service.Find(query, maxResults);
    public SymbolSearchResult Resolve(string query, bool callableOnly = false, int maxResults = QueryLimits.DefaultMaxResults) => service.ResolveSymbol(query, callableOnly, maxResults);
    public IndexedSymbol? FindById(string id) => service.FindById(id);
    public IReadOnlyDictionary<string, IndexedSymbol> FindByIds(IEnumerable<string> ids) => service.FindByIds(ids);
    public IReadOnlyList<IndexedSymbol> Members(IndexedSymbol symbol, int maxResults = QueryLimits.DefaultMemberMaxResults) => service.Members(symbol, maxResults);
    public IReadOnlyList<IndexedSymbol> InFiles(IReadOnlyCollection<string> relativePaths, int maxResults = 500) => service.SymbolsInFiles(relativePaths, maxResults);
}

public sealed class RelationQueryService(CodeMapQueryService service)
{
    public IReadOnlyList<IndexedRelation> Callers(IndexedSymbol symbol, int maxResults = QueryLimits.DefaultMaxResults) => service.CallerRelations(symbol, maxResults);
    public IReadOnlyList<IndexedRelation> Callees(IndexedSymbol symbol, int depth = 1, int maxResults = QueryLimits.DefaultMaxResults) => service.CalleeRelations(symbol, depth, maxResults);
    public IReadOnlyList<IndexedRelation> Implementations(IndexedSymbol symbol, int maxResults = QueryLimits.DefaultMaxResults) => service.ImplementationRelations(symbol, maxResults);
    public IReadOnlyList<RelationQueryResult> Between(string sourceId, string targetId, int maxResults = QueryLimits.DefaultMaxResults, double minConfidence = QueryLimits.DefaultMinConfidence) => service.Relations(sourceId, targetId, null, maxResults, minConfidence);
}

public sealed class ImpactQueryService(CodeMapQueryService service)
{
    public IReadOnlyList<ImpactItem> Query(IndexedSymbol symbol, int depth, int maxResults, string profile = "code") => service.Impact(symbol, depth, maxResults, profile);
    public IReadOnlyList<ImpactItem> QueryUnion(IEnumerable<IndexedSymbol> roots, int depth, int maxResults, string profile = "code") => service.ImpactUnion(roots, depth, maxResults, profile);
}

public sealed class FlowQueryService(CodeMapQueryService service)
{
    public IReadOnlyList<ImpactItem> Query(IndexedSymbol entry, string kind = "all", int depth = QueryLimits.FlowDefaultDepth, int maxResults = QueryLimits.DefaultMaxResults, double minConfidence = QueryLimits.DefaultMinConfidence) => service.Flow(entry, kind, depth, maxResults, minConfidence);
}

public sealed class RepositoryMapService(CodeMapQueryService service)
{
    public RepoMap Build(string? focus = null, string? project = null, int tokenBudget = 500) => service.BuildMap(focus, project, tokenBudget);
}
