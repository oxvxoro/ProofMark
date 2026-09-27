using CodeMap.Core.Models;
using CodeMap.Storage;
using Proof.Adapters.CodeMap;
using Proof.Adapters.CodeMap.Sessions;
using Proof.Core;
using Proof.Tests.Fakes;

namespace Proof.Tests;

/// <summary>
/// caller/deletion 순회의 완전성 정확성: 분석 예산이 소진되어 멈춘
/// 순회는 절대 완료로 보고되면 안 된다.
/// </summary>
public sealed class CodeMapCallerCompletenessTests
{
    // T1 — 첫 루트가 예산을 정확히 소진하고, 이후 루트는 절대
    // 검사되지 않는다. 방문하지 않은 루트는 완전성이 아니라 절단이다.
    [Fact]
    public void CollectCallers_FirstRootExhaustsBudget_WithRootRemaining_IsTruncated()
    {
        var rootA = Symbol("a", "A", "App/A.cs");
        var rootB = Symbol("b", "B", "App/B.cs");
        var reader = new FakeCodeMapGraphReader(
            [rootA, rootB],
            PagedCallers(new Dictionary<string, IndexedRelation[]>
            {
                ["a"] = [Relation(rootA, "c1"), Relation(rootA, "c2")],
                ["b"] = [Relation(rootB, "c3")]
            }));

        var (subjectIds, callers, truncated) = RunCallers(reader, [rootA, rootB], pageSize: 2, max: 2);

        Assert.True(truncated);
        Assert.Equal(2, callers.Count);
        Assert.Equal(2, subjectIds.Count);
    }

    // T2 — 더 이상 페이지가 없이 예산을 정확히 소진한 단일 루트는
    // 완료이다.
    [Fact]
    public void CollectCallers_SingleRootExhaustsBudget_WithNoMore_IsNotTruncated()
    {
        var root = Symbol("a", "A", "App/A.cs");
        var reader = new FakeCodeMapGraphReader(
            [root],
            PagedCallers(new Dictionary<string, IndexedRelation[]>
            {
                ["a"] = [Relation(root, "c1"), Relation(root, "c2")]
            }));

        var (_, callers, truncated) = RunCallers(reader, [root], pageSize: 2, max: 2);

        Assert.False(truncated);
        Assert.Equal(2, callers.Count);
    }

    // T3 — 현재 루트에 caller가 더 남은 상태에서 예산에 도달한다.
    [Fact]
    public void CollectCallers_CurrentRootHasMore_AtBudget_IsTruncated()
    {
        var root = Symbol("a", "A", "App/A.cs");
        var reader = new FakeCodeMapGraphReader(
            [root],
            PagedCallers(new Dictionary<string, IndexedRelation[]>
            {
                ["a"] = [Relation(root, "c1"), Relation(root, "c2"), Relation(root, "c3")]
            }));

        var (_, callers, truncated) = RunCallers(reader, [root], pageSize: 2, max: 2);

        Assert.True(truncated);
        Assert.Equal(2, callers.Count);
    }

    // T4 — 결과는 더 보고하지만 진전은 없는 리더.
    [Fact]
    public void CollectCallers_ZeroProgressPage_IsTruncated()
    {
        var root = Symbol("a", "A", "App/A.cs");
        var reader = new FakeCodeMapGraphReader(
            [root],
            (_, _, _) => new RelationPage<IndexedRelation>([], HasMore: true));

        var (_, callers, truncated) = RunCallers(reader, [root], pageSize: 50, max: 50);

        Assert.True(truncated);
        Assert.Empty(callers);
    }

    // T5 — deletion caller 예산 초과.
    [Fact]
    public void AnalyzeDeletion_CallerBudgetExceeded_FlagsTruncation()
    {
        var gone = Symbol("gone", "Gone", "App/Gone.cs");
        var callers = Enumerable.Range(0, 3)
            .Select(index => Symbol($"caller-{index}", $"Caller{index}", $"App/Caller{index}.cs"))
            .ToArray();
        var snapshot = new CodeMapSnapshot
        {
            Files = [],
            Symbols = [gone, .. callers],
            Edges = callers
                .Select(caller => new IndexedEdge(caller.Id, gone.Id, EdgeKind.Calls, null, 1))
                .ToArray()
        };

        var result = DeletionImpactAnalyzer.Analyze(
            new CodeMapQueryService(snapshot),
            ["App/Gone.cs"],
            new ResolvedImpactBudget(1, 100, 2, 2, 0),
            []);

        Assert.NotNull(result);
        Assert.True(result!.CallerPotentiallyTruncated);
        Assert.Equal(2, result.CallerRelations.Count);
    }

    // T6 — 삭제된 심볼 개수 초과.
    [Fact]
    public void AnalyzeDeletion_SymbolBudgetExceeded_FlagsTruncation()
    {
        var symbols = Enumerable.Range(0, 3)
            .Select(index => Symbol($"gone-{index}", $"Gone{index}", "App/Gone.cs"))
            .ToArray();
        var snapshot = new CodeMapSnapshot { Files = [], Symbols = symbols, Edges = [] };

        var result = DeletionImpactAnalyzer.Analyze(
            new CodeMapQueryService(snapshot),
            ["App/Gone.cs"],
            new ResolvedImpactBudget(1, 2, 50, 50, 0),
            []);

        Assert.NotNull(result);
        Assert.True(result!.ImpactPotentiallyTruncated);
        Assert.Equal(2, result.DeletedSymbols.Count);
    }

    // T7 — head 순회는 완료이고 deletion은 절단이다. 최상위 완전성은
    // 여전히 절단을 보고해야 한다.
    [Fact]
    public async Task Build_DeletionTruncated_PropagatesToCompleteness()
    {
        var root = Symbol("root", "Root", "App/Root.cs");
        var deletion = new DeletionImpactResult(
            ["App/Gone.cs"],
            [],
            [],
            [],
            ImpactPotentiallyTruncated: false,
            CallerPotentiallyTruncated: true);
        var request = new ChangeRequest(Path.GetTempPath(), "base", "head", []);

        var impact = await ImpactWalkSession.BuildAsync(
            request,
            [root],
            [Changed(root)],
            deletion,
            new FakeCodeMapGraphReader([root]),
            new ResolvedImpactBudget(1, 100, 50, 50, 0),
            "code",
            unknownSpanCount: 0,
            new TestProjectClassifier(Path.GetTempPath(), null),
            runArchitectureCheck: false,
            Path.GetTempPath(),
            databasePath: "index.db",
            CancellationToken.None);

        Assert.NotNull(impact.Completeness);
        Assert.Equal(CoverageState.PotentiallyTruncated, impact.Completeness!.Traversal);
        Assert.True(impact.Completeness.CallerPotentiallyTruncated);
        Assert.False(impact.Completeness.ImpactPotentiallyTruncated);
        Assert.Contains(
            impact.Constraints!,
            constraint => constraint.Code == ProofReasonCodes.CallerPotentiallyTruncated);
    }

    private static (List<string> SubjectIds, List<CallerRelation> Callers, bool Truncated) RunCallers(
        FakeCodeMapGraphReader reader,
        IReadOnlyList<IndexedSymbol> roots,
        int pageSize,
        int max)
    {
        var subjectIds = new List<string>();
        var callers = new List<CallerRelation>();
        var truncated = ImpactWalkSession.CollectCallers(
            reader,
            roots,
            new ResolvedImpactBudget(1, 100, pageSize, max, 0),
            _ => false,
            subjectIds,
            callers);
        return (subjectIds, callers, truncated);
    }

    private static Func<IndexedSymbol, int, int, RelationPage<IndexedRelation>> PagedCallers(
        Dictionary<string, IndexedRelation[]> relations)
        => (symbol, limit, offset) =>
        {
            var all = relations.GetValueOrDefault(symbol.Id, []);
            var items = all.Skip(offset).Take(limit).ToArray();
            var hasMore = offset + items.Length < all.Length;
            return new RelationPage<IndexedRelation>(items, hasMore);
        };

    private static IndexedRelation Relation(IndexedSymbol target, string callerId)
    {
        var caller = Symbol(callerId, callerId, $"App/{callerId}.cs");
        return new IndexedRelation(caller, new IndexedEdge(caller.Id, target.Id, EdgeKind.Calls, null, 1));
    }

    private static ChangedSymbolRef Changed(IndexedSymbol symbol) =>
        new(
            symbol.Id,
            symbol.Project,
            symbol.RelativePath,
            symbol.DisplayName,
            symbol.StartLine,
            symbol.EndLine,
            true,
            false);

    private static IndexedSymbol Symbol(string id, string name, string relativePath) =>
        new(id, "App", "file-" + id, relativePath, NodeKind.Method, name, $"App.{name}", null, 1, 10, "public", "csharp");
}
