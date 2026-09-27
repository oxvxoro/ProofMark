namespace CodeMap.Storage.Queries;

/// <summary>
/// 질의 애플리케이션 서비스가 사용하는 백엔드 독립 옵션.
/// 기존 <see cref="CodeMap.Storage.CodeMapQueryService"/> 오버로드는
/// 호출자가 이 계약으로 점진적으로 옮기는 동안에도 계속 사용할 수 있다.
/// </summary>
public sealed record QueryOptions(
    int MaxResults = QueryLimits.DefaultMaxResults,
    int Depth = 1,
    double MinConfidence = QueryLimits.DefaultMinConfidence);

public sealed record RelationQuery(
    string SourceId,
    string TargetId,
    string? EdgeKind = null,
    QueryOptions? Options = null);

public sealed record TraversalQuery(
    string SymbolId,
    int Depth = 1,
    QueryOptions? Options = null);

public sealed record ImpactQuery(
    IReadOnlyList<string> RootIds,
    int Depth = 1,
    string Profile = "code",
    QueryOptions? Options = null);

public sealed record FlowQuery(
    string EntryId,
    string Kind = "all",
    int Depth = QueryLimits.FlowDefaultDepth,
    QueryOptions? Options = null);
