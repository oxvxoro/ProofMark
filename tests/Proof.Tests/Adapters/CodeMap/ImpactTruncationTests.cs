using CodeMap.Core.Models;
using CodeMap.Storage;
using Proof.Adapters.CodeMap;

namespace Proof.Tests;

public sealed class ImpactTruncationTests
{
    [Fact]
    public void CollectRootedImpact_TwoRootsExceedingMaxResults_IsNotTruncated_WhenHasMoreIsFalse()
    {
        var rootA = Symbol("root-a", "RootA", "App/A.cs");
        var rootB = Symbol("root-b", "RootB", "App/B.cs");
        var impacted = Enumerable.Range(0, 260)
            .Select(index => Symbol($"imp-{index}", $"Imp{index}", $"App/Imp{index}.cs"))
            .ToArray();

        var edges = impacted
            .Select(symbol => new IndexedEdge(symbol.Id, rootA.Id, EdgeKind.References, null, 1))
            .Concat(impacted.Select(symbol => new IndexedEdge(symbol.Id, rootB.Id, EdgeKind.References, null, 1)))
            .ToArray();

        var snapshot = new CodeMapSnapshot
        {
            Files = [],
            Symbols = [rootA, rootB, .. impacted],
            Edges = edges
        };

        var reader = new CodeMapQueryService(snapshot);
        var budget = new ResolvedImpactBudget(2, 500, 50, 50, 0);
        var (items, truncated) = CodeMapChangeImpactProvider.CollectRootedImpact(
            reader,
            [rootA, rootB],
            budget,
            "code");

        Assert.False(truncated);
        Assert.True(items.Count >= 500);
    }

    [Fact]
    public void CollectRootedImpact_PageHasMore_IsTruncated()
    {
        var root = Symbol("root", "Root", "App/Root.cs");
        var impacted = Enumerable.Range(0, 3)
            .Select(index => Symbol($"imp-{index}", $"Imp{index}", $"App/Imp{index}.cs"))
            .ToArray();
        var snapshot = new CodeMapSnapshot
        {
            Files = [],
            Symbols = [root, .. impacted],
            Edges = impacted
                .Select(symbol => new IndexedEdge(symbol.Id, root.Id, EdgeKind.References, null, 1))
                .ToArray()
        };

        var (items, truncated) = CodeMapChangeImpactProvider.CollectRootedImpact(
            new CodeMapQueryService(snapshot),
            [root],
            new ResolvedImpactBudget(2, 2, 50, 50, 0),
            "code");

        Assert.Equal(3, items.Count);
        Assert.True(truncated);
    }

    private static IndexedSymbol Symbol(string id, string name, string relativePath) =>
        new(id, "App", "file-" + id, relativePath, NodeKind.Method, name, $"App.{name}", null, 1, 10, "public", "csharp");
}
