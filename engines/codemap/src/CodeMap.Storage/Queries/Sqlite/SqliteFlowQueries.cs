using CodeMap.Core.Models;

namespace CodeMap.Storage.Queries.Sqlite;

/// <summary>SQLite 백엔드의 흐름 전용 어댑터.</summary>
internal sealed class SqliteFlowQueries(CodeMapQueryService service)
{
    public IReadOnlyList<ImpactItem> Query(IndexedSymbol entry, string kind, int depth, int maxResults, double minConfidence) => service.Flow(entry, kind, depth, maxResults, minConfidence);
}
