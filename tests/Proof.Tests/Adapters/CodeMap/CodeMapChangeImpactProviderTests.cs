using CodeMap.Core.Models;
using CodeMap.Engine.Application;
using Proof.Adapters.CodeMap;
using Proof.Adapters.CodeMap.Sessions;
using Proof.Core;

namespace Proof.Tests;

public sealed class CodeMapChangeImpactProviderTests
{
    [Fact]
    public void SelectIndexableSpans_SkipsNonSourceFilesButKeepsUnmatchedSourceFiles()
    {
        var projects = new CodeMapProjectPathMap.Project[]
        {
            new("App", "src/App")
        };

        var spans = ImpactChangedSymbolLocator.SelectIndexableSpans(
        [
            new LineSpan("proof.yml", 1, 2),
            new LineSpan(".cursor/scripts/publish.ps1", 3, 4),
            new LineSpan("src/App/Program.cs", 5, 6),
            new LineSpan("src/Unknown/Missing.cs", 7, 8)
        ],
        projects);

        Assert.Collection(
            spans,
            span => Assert.Equal(new ChangedFileSpan("App/Program.cs", 5, 6), span),
            span => Assert.Equal(new ChangedFileSpan("src/Unknown/Missing.cs", 7, 8), span));
    }

    [Fact]
    public void SelectCallerSubjectIds_UsesRelationSymbolId()
    {
        var changed = Symbol("changed", "PaymentService.Cancel");
        var callerA = Symbol("caller-a", "OrderController.Cancel");
        var callerB = Symbol("caller-b", "RetryJob.Execute");
        var relations = new Dictionary<string, IndexedRelation[]>(StringComparer.Ordinal)
        {
            [changed.Id] =
            [
                new IndexedRelation(callerA, new IndexedEdge(callerA.Id, changed.Id, EdgeKind.Calls, callerA.FileId, 10)),
                new IndexedRelation(callerB, new IndexedEdge(callerB.Id, changed.Id, EdgeKind.Calls, callerB.FileId, 20))
            ]
        };

        var ids = CodeMapChangeImpactProvider.SelectCallerSubjectIds(
            [changed],
            symbol => relations.GetValueOrDefault(symbol.Id) ?? []);

        Assert.Equal(["caller-a", "caller-b"], ids);
        Assert.DoesNotContain("changed", ids);
    }

    private static IndexedSymbol Symbol(string id, string name)
        => new(id, "App", "file-1", "App/File.cs", NodeKind.Method, name, name, "()", 1, 10, "public", "csharp");
}
