using Proof.Adapters.CodeMap;
using Proof.Cli;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

public sealed class MapSuggestTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "proof-map-suggest-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workspace, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private static ChangeImpact Impact(params ImpactedSymbolRef[] impacted)
        => new(
            "main",
            "WORKTREE",
            [],
            [new ChangedSymbolRef("s1", "App", "App/OrderService.cs", "OrderService.Cancel", 1, 10, IsPublic: false, IsTest: false)],
            impacted,
            ["App"],
            [],
            "complete",
            HasHeuristicEdges: false);

    // RootChangedSymbolId는 변경된 심볼과 일부러 다르다. 같은 루트의
    // 영향받는 테스트는 P005를 억제하고, 이 표면은
    // *열린* P005 의무에 대한 제안만 나열한다.
    private static ImpactedSymbolRef TestSymbol(string id, string file, string displayName)
        => new(id, "App.Tests", file, displayName, "other-root", 1, IsTest: true, ResolutionKind: "contains", Confidence: 1.0);

    [Fact]
    public void Suggest_SameFileTestSymbol_ProposesMapEntry()
    {
        var impact = Impact(TestSymbol("t1", "App/OrderService.cs", "OrderServiceTests.Cancel_keeps_order"));
        var plan = new DeterministicProofPlanner().Plan(impact);

        var suggestions = MapSuggester.Suggest(impact, plan);

        var suggestion = Assert.Single(suggestions);
        Assert.Equal("s1", suggestion.Symbol.SubjectId);
        Assert.Contains(suggestion.Tests, test => test.Contains("Cancel_keeps_order", StringComparison.Ordinal));
        Assert.Contains(suggestion.Reasons, reason => reason.Contains("same file", StringComparison.Ordinal));
    }

    [Fact]
    public void Suggest_DisplayNameTokenMatch_ProposesMapEntry()
    {
        var impact = Impact(TestSymbol("t1", "Tests/Other.cs", "Proof.Tests.OrderServiceTests.Cancel_keeps_order"));
        var plan = new DeterministicProofPlanner().Plan(impact);

        var suggestions = MapSuggester.Suggest(impact, plan);

        var suggestion = Assert.Single(suggestions);
        Assert.Contains(suggestion.Tests, test => test.Contains("Cancel_keeps_order", StringComparison.Ordinal));
        Assert.Contains(suggestion.Reasons, reason => reason.Contains("display name contains", StringComparison.Ordinal));
    }

    [Fact]
    public void Suggest_ShortBareMethodNamesNeverMatch()
    {
        var impact = Impact(TestSymbol("t1", "Tests/Other.cs", "Do_it_now"));
        var plan = new DeterministicProofPlanner().Plan(impact);

        var suggestions = MapSuggester.Suggest(impact, plan);

        var suggestion = Assert.Single(suggestions);
        Assert.Empty(suggestion.Tests);
    }

    [Fact]
    public void ToYamlSnippet_RendersPasteableTestMaps()
    {
        var impact = Impact(TestSymbol("t1", "App/OrderService.cs", "OrderServiceTests.Cancel_keeps_order"));
        var plan = new DeterministicProofPlanner().Plan(impact);

        var yaml = MapSuggester.ToYamlSnippet(MapSuggester.Suggest(impact, plan));

        Assert.StartsWith("policy:", yaml, StringComparison.Ordinal);
        Assert.Contains("- symbol: \"OrderService.Cancel\"", yaml, StringComparison.Ordinal);
        Assert.Contains("OrderServiceTests.Cancel_keeps_order", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Suggest_DoesNotWriteTestMaps()
    {
        Directory.CreateDirectory(_workspace);
        var original = "version: 2\nproof:\n  base:\n    strategy: mergeBase\n";
        var configPath = Path.Combine(_workspace, "proof.yml");
        File.WriteAllText(configPath, original);

        var impact = Impact(TestSymbol("t1", "App/OrderService.cs", "OrderServiceTests.Cancel_keeps_order"));
        var plan = new DeterministicProofPlanner().Plan(impact);

        // 제안 출력은 비활성이다. 헬퍼를 실행해도 구성과
        // 워크스페이스는 그대로다(suggest는 조언이며 절대 증거가 아니다).
        var suggestions = MapSuggester.Suggest(impact, plan);
        var snippet = MapSuggester.ToYamlSnippet(suggestions);

        Assert.NotEmpty(snippet);
        Assert.Equal(original, File.ReadAllText(configPath));
        Assert.DoesNotContain(
            Directory.GetFiles(_workspace, "*", SearchOption.AllDirectories),
            path => !path.EndsWith("proof.yml", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Suggestions_AreNotEvidence_WithoutExplicitConfig()
    {
        // 엔진은 policy.testMaps만 소비한다. 제안은 사람이 구성에 붙여 넣지
        // 않는 한 절대 증거가 되지 않으므로, 빈 맵은 제안이
        // 있어도 P005를 미증명으로 남긴다.
        var impact = Impact(TestSymbol("t1", "App/OrderService.cs", "OrderServiceTests.Cancel_keeps_order"));
        var plan = new DeterministicProofPlanner().Plan(impact);
        Assert.NotEmpty(MapSuggester.Suggest(impact, plan));

        var producer = new YamlTestMappingEvidenceProducer(null);
        var evidence = await producer.AnalyzeAsync(
            new ChangeRequest("root", "base", "head", [], SourceDigest: "src"),
            impact,
            plan,
            CancellationToken.None);

        Assert.Empty(evidence);
    }

    [Fact]
    public void BuildResult_AlwaysFlagsMustReview()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            MapSuggestCommand.BuildResult([]),
            ProofJson.WireOptions);

        Assert.Contains("\"mustReview\": true", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Suggest_NoOpenP005WhenTestCallerExists()
    {
        // 테스트 caller는 플래너에서 P005를 억제하므로 제안이
        // 전혀 나오지 않는다. 제안 표면은 정직하게 남는다.
        var impact = new ChangeImpact(
            "main",
            "WORKTREE",
            [new LineSpan("App/OrderService.cs", 1, 10)],
            [new ChangedSymbolRef("s1", "App", "App/OrderService.cs", "OrderService.Cancel", 1, 10, IsPublic: false, IsTest: false)],
            [],
            ["App"],
            [],
            "complete",
            HasHeuristicEdges: false,
            Callers: [new CallerRelation("s1", "t1", "App.Tests", "App/OrderServiceTests.cs", "Calls", 1.0, IsTest: true)]);

        var plan = new DeterministicProofPlanner().Plan(impact);

        Assert.Empty(MapSuggester.Suggest(impact, plan));
    }
}
