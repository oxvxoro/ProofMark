using CodeMap.Core.Contracts;
using CodeMap.Core.Models;
using CodeMap.Storage;
using Proof.Adapters.CodeMap;
using Proof.Core;

namespace Proof.Tests;

public sealed class DeletionImpactAnalyzerTests
{
    [Fact]
    public void CollectDeletedPaths_NormalizesSlashes_AndCoversRenames()
    {
        var deltas = new FileDelta[]
        {
            new(FileChangeKind.Deleted, @"App\Gone.cs", null, [], []),
            new(FileChangeKind.Renamed, "App/Old.cs", "App/New.cs", [], []),
            new(FileChangeKind.Modified, "App/Kept.cs", "App/Kept.cs", [], [])
        };

        var paths = DeletionImpactAnalyzer.CollectDeletedPaths(deltas);

        Assert.Equal(["App/Gone.cs", "App/Old.cs"], paths);
    }

    [Fact]
    public void CollectDeletedPaths_ReturnsEmpty_WhenNoDeltas()
    {
        Assert.Empty(DeletionImpactAnalyzer.CollectDeletedPaths(null));
        Assert.Empty(DeletionImpactAnalyzer.CollectDeletedPaths([]));
    }

    [Fact]
    public void Analyze_RecoversSymbolsAndCallersFromBaseGraph()
    {
        var gone = Symbol("gone-1", "Gone", "App/Gone.cs");
        var caller = Symbol("caller-1", "Caller", "App/Caller.cs");
        var snapshot = new CodeMapSnapshot
        {
            Files = [],
            Symbols = [gone, caller],
            Edges = [new IndexedEdge(caller.Id, gone.Id, EdgeKind.Calls, null, 1)]
        };

        var reader = new CodeMapQueryService(snapshot);
        var result = DeletionImpactAnalyzer.Analyze(
            reader,
            ["App/Gone.cs"],
            new ResolvedImpactBudget(2, 100, 50, 50, 0),
            []);

        Assert.NotNull(result);
        Assert.Equal(["App/Gone.cs"], result.ResolvedPaths);
        Assert.Contains(result.DeletedSymbols, symbol => symbol.Id == gone.Id);
        var callerRelation = Assert.Single(result.CallerRelations, relation => relation.CallerSymbolId == caller.Id);
        Assert.Equal(gone.Id, callerRelation.ChangedSymbolId);
    }

    [Fact]
    public void Analyze_ReturnsNull_WhenBaseGraphHasNoSymbolsForPath()
    {
        var snapshot = new CodeMapSnapshot { Files = [], Symbols = [], Edges = [] };
        var reader = new CodeMapQueryService(snapshot);

        var result = DeletionImpactAnalyzer.Analyze(
            reader,
            ["App/Gone.cs"],
            new ResolvedImpactBudget(2, 100, 50, 50, 0),
            []);

        Assert.Null(result);
    }

    [Fact]
    public void Analyze_ExcludesHeadChangedSymbolsFromDeletedSymbols()
    {
        var gone = Symbol("gone-1", "Gone", "App/Gone.cs");
        var snapshot = new CodeMapSnapshot { Files = [], Symbols = [gone], Edges = [] };
        var reader = new CodeMapQueryService(snapshot);

        var result = DeletionImpactAnalyzer.Analyze(
            reader,
            ["App/Gone.cs"],
            new ResolvedImpactBudget(2, 100, 50, 50, 0),
            ["gone-1"]);

        Assert.NotNull(result);
        Assert.Equal(["App/Gone.cs"], result.ResolvedPaths);
        Assert.DoesNotContain(result.DeletedSymbols, symbol => symbol.Id == "gone-1");
    }

    [Fact]
    public void Analyze_UsesProvidedTestClassifier()
    {
        var gone = Symbol("gone-1", "Gone", "App/Gone.cs");
        var caller = Symbol("caller-1", "Caller", "App/Caller.cs");
        var snapshot = new CodeMapSnapshot
        {
            Files = [],
            Symbols = [gone, caller],
            Edges = [new IndexedEdge(caller.Id, gone.Id, EdgeKind.Calls, null, 1)]
        };

        var result = DeletionImpactAnalyzer.Analyze(
            new CodeMapQueryService(snapshot),
            ["App/Gone.cs"],
            new ResolvedImpactBudget(2, 100, 50, 50, 0),
            [],
            symbol => symbol.Id == caller.Id);

        Assert.NotNull(result);
        Assert.True(result!.CallerRelations.Single().IsTest);
    }

    private static IndexedSymbol Symbol(string id, string name, string relativePath) =>
        new(id, "App", "file-1", relativePath, NodeKind.Method, name, $"App.{name}", null, 1, 10, "public", "csharp");
}
