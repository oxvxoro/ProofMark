namespace CodeMap.Storage.Queries;

/// <summary>
/// 모든 질의 진입점이 공유하는 기본값과 정규화 규칙.
/// 이 값을 한곳에 두면 리팩터링 중에 스냅샷 경로와 SQLite 경로가
/// 서로 다른 암묵적 한도를 갖게 되는 일을 막는다.
/// </summary>
public static class QueryLimits
{
    public const int DefaultMaxResults = 20;
    public const int DefaultSymbolsInFilesMaxResults = 500;
    public const int DefaultMemberMaxResults = 8;

    public const int FlowMinDepth = 1;
    public const int FlowMaxDepth = 8;
    public const int FlowDefaultDepth = 4;

    public const double DefaultMinConfidence = 0;

    public static int NormalizeMaxResults(int value) => Math.Max(1, value);

    public static int NormalizeImpactDepth(int value) => Math.Max(0, value);

    public static int NormalizeTraversalDepth(int value) => Math.Max(1, value);

    public static int ClampFlowDepth(int value) => Math.Clamp(value, FlowMinDepth, FlowMaxDepth);

    public static int NormalizeTokenBudget(int value) => Math.Max(1, value);
}
