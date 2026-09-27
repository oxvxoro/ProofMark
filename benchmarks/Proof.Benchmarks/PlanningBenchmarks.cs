using BenchmarkDotNet.Attributes;
using Proof.Core;
using Proof.Engine;

namespace Proof.Benchmarks;

/// <summary>
/// PR-04 PlanningIndex가 겨냥하는 행렬에 걸친 플래너 비용: 변경된
/// 심볼 수, 영향받은 심볼 수, caller/impact 관계 수.
/// </summary>
[MemoryDiagnoser]
public class PlanningBenchmarks
{
    [Params(10, 100, 1000)]
    public int ChangedCount { get; set; }

    [Params(1000, 10000)]
    public int ImpactedCount { get; set; }

    [Params(10000, 100000)]
    public int RelationCount { get; set; }

    private ChangeImpact _impact = null!;
    private ProofPolicy _policy = null!;

    [GlobalSetup]
    public void Setup()
    {
        var changed = Enumerable.Range(0, ChangedCount)
            .Select(index => new ChangedSymbolRef(
                $"c{index}", "App", $"App/C{index}.cs", $"C{index}.M", 1, 2, IsPublic: index % 3 == 0, IsTest: false))
            .ToArray();

        var impacted = Enumerable.Range(0, ImpactedCount)
            .Select(index =>
            {
                var root = changed[index % changed.Length];
                return new ImpactedSymbolRef(
                    $"i{index}",
                    index % 5 == 0 ? "App.Tests" : "App",
                    $"App/X{index}.cs",
                    $"X{index}.M",
                    root.Id,
                    Depth: 1,
                    IsTest: index % 10 == 0,
                    ResolutionKind: "Semantic",
                    Confidence: 1.0);
            })
            .ToArray();

        var relations = Enumerable.Range(0, RelationCount)
            .Select(index =>
            {
                var symbol = impacted[index % impacted.Length];
                return new ImpactRelation(symbol.RootChangedSymbolId, symbol.Id, 1, "Calls", "Semantic", 1.0);
            })
            .ToArray();

        var callers = Enumerable.Range(0, ImpactedCount)
            .Select(index => new CallerRelation(
                changed[index % changed.Length].Id,
                $"caller{index}",
                "App",
                $"App/Caller{index}.cs",
                "Calls",
                1.0,
                IsTest: index % 4 == 0))
            .ToArray();

        _impact = new ChangeImpact(
            "base",
            "head",
            [],
            changed,
            impacted,
            ["App", "App.Tests"],
            [],
            "complete",
            false,
            Relations: relations,
            Callers: callers,
            SourceDigest: "bench");
        _policy = new ProofPolicy(TestMaps: [new TestMapEntry("C0.M", ["T0"])]);
    }

    [Benchmark]
    public int Plan() => new DeterministicProofPlanner().Plan(_impact, _policy).Obligations.Count;
}
