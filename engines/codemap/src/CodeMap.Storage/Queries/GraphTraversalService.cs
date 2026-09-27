using CodeMap.Core.Models;

namespace CodeMap.Storage.Queries;

public sealed record GraphTraversalResult(string SymbolId, IndexedEdge Via, int Depth);

/// <summary>그래프 유스케이스가 공유하는 결정적이고 순환에 안전한 순회 기본 연산.</summary>
public static class GraphTraversalService
{
    public static IReadOnlyList<GraphTraversalResult> Traverse(
        string rootId,
        Func<string, IEnumerable<IndexedEdge>> adjacency,
        Func<string, IndexedSymbol?> resolve,
        int maxDepth,
        int maxResults,
        double minConfidence = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootId);
        ArgumentNullException.ThrowIfNull(adjacency);
        ArgumentNullException.ThrowIfNull(resolve);
        var result = new List<GraphTraversalResult>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { rootId };
        var frontier = new[] { rootId };
        for (var depth = 1; depth <= Math.Max(0, maxDepth) && frontier.Length > 0; depth++)
        {
            var next = new List<string>();
            foreach (var sourceId in frontier.OrderBy(id => id, StringComparer.Ordinal))
            {
                foreach (var edge in adjacency(sourceId)
                    .Where(edge => (edge.Confidence ?? 1) >= minConfidence)
                    .OrderBy(edge => edge.TargetId, StringComparer.Ordinal)
                    .ThenBy(edge => edge.Kind)
                    .ThenBy(edge => edge.Line ?? int.MaxValue))
                {
                    if (!visited.Add(edge.TargetId) || resolve(edge.TargetId) is null)
                        continue;
                    result.Add(new GraphTraversalResult(edge.TargetId, edge, depth));
                    next.Add(edge.TargetId);
                    if (result.Count >= Math.Max(1, maxResults))
                        return result;
                }
            }
            frontier = next.ToArray();
        }
        return result;
    }
}
