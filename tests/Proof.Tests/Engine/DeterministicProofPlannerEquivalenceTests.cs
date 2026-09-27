using System.Text;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

/// <summary>
/// 풍부한 영향 픽스처에 대한 플래너의 순서까지 포함한 전체 출력을 고정한다.
/// PR-03 인덱싱 리팩터는 의무 id, 순서, claim,
/// subject, reason, constraint를 바꾸면 안 된다.
/// </summary>
public sealed class DeterministicProofPlannerEquivalenceTests
{
    [Fact]
    public void Plan_RichFixture_ProducesStableCanonicalOutput()
    {
        var plan = new DeterministicProofPlanner().Plan(BuildImpact(), BuildPolicy());
        var actual = Canonicalize(plan).Replace("\r\n", "\n");

        Assert.Equal(Expected, actual);
    }

    private static readonly string Expected = string.Join('\n',
    [
        "O-P001A-74e3c952|P001A|Compatibility|Public API surface of 'Legacy.Do' remains compatible|pub-1|True|4|changed public symbol in App|ApiSurface|pub-1|App|App/Legacy.cs|Legacy.Do",
        "O-P001B-0677cba5|P001B|Build|Internal consumer project 'App' compiles after public API change|App|True|3|impacted internal consumer must compile|Project|App|App||",
        "O-P001B-79543b8d|P001B|Build|Internal consumer project 'Consumer' compiles after public API change|Consumer|True|3|impacted internal consumer must compile|Project|Consumer|Consumer||",
        "O-P002-e66e7c07|P002|CallerContract|Caller contract for 'test-caller-1' remains valid|test-caller-1|True|3|CodeMap detected caller relation|Symbol|test-caller-1|App.Tests|tests/App.Tests/LegacyTests.cs|test-caller-1",
        "O-P004-71621a19|P004|Test|Mapped test 'App.Tests.LegacyTests.Do_still_calls' remains valid|App.Tests.LegacyTests.Do_still_calls|True|4|explicit test map entry|Test|App.Tests.LegacyTests.Do_still_calls|||App.Tests.LegacyTests.Do_still_calls",
        "O-P004-d2d2b302|P004|Test|Mapped test 'App.Tests.ServiceTests.Compute_ok' remains valid|App.Tests.ServiceTests.Compute_ok|True|4|explicit test map entry|Test|App.Tests.ServiceTests.Compute_ok|||App.Tests.ServiceTests.Compute_ok",
        "O-P004-e38572ed|P004|Test|Caller test 'test-caller-1' remains valid|test-caller-1|True|4|CodeMap found caller relation from a test symbol|Test|test-caller-1|App.Tests|tests/App.Tests/LegacyTests.cs|test-caller-1",
        "O-P004-415e5446|P004|Test|Impacted test 'LegacyTests.Do' remains valid|test-imp-1|True|4|CodeMap found impacted test symbol|Test|test-imp-1|App.Tests|tests/App.Tests/LegacyTests.cs|LegacyTests.Do",
        "O-P005-e996bcc1|P005|TestMapping|Changed symbol 'Other.Run' has a mapped verification test|dup-1|True|2|no mapped test relation found|Symbol|dup-1|App|App/Other.cs|Other.Run",
        "O-P005-e996bcc1|P005|TestMapping|Changed symbol 'Other.Run' has a mapped verification test|dup-1|True|2|no mapped test relation found|Symbol|dup-1|App|App/Other.cs|Other.Run",
        "O-P006-dabbc547|P006|Uncertainty|Caller collection reached a result limit|CALLER_POTENTIALLY_TRUNCATED|True|1|CALLER_POTENTIALLY_TRUNCATED|Repository|CALLER_POTENTIALLY_TRUNCATED|||Caller collection reached a result limit",
        "O-P009-fcf8ab42|P009|ManualReview|Deletion of 'App/Untraced.cs' cannot be traced to callers in the base graph; a signed manual review must confirm no references remain.|App/Untraced.cs|True|3|CHANGE_DELETION_ANALYSIS_UNAVAILABLE|File|App/Untraced.cs||App/Untraced.cs|App/Untraced.cs",
        "O-P009-386e63de|P009|ManualReview|Binary change 'assets/logo.png' has no semantic impact analysis; a signed manual review is required.|assets/logo.png|True|3|CHANGE_BINARY_ANALYSIS_UNAVAILABLE|File|assets/logo.png||assets/logo.png|assets/logo.png",
        "O-P009-9a786b4d|P009|ManualReview|Change to 'docs/design.md' is governed by a manual-review path rule.|docs/design.md|True|3|MANUAL_REVIEW_REQUIRED|File|docs/design.md||docs/design.md|docs/design.md",
        "O-P009-f7f05025|P009|ManualReview|Submodule change 'vendor/lib' is unsupported; a signed manual review is required.|vendor/lib|True|3|CHANGE_SUBMODULE_ANALYSIS_UNAVAILABLE|File|vendor/lib||vendor/lib|vendor/lib",
        "O-P010-529d74fb|P010|AppContract|App contract for 'Endpoints.Route' remains valid (RoutesTo)|app-route|True|2|app-graph edge under analysis.impact.profile=app|Symbol|app-route|App|App/Endpoints.cs|Endpoints.Route",
        "O-P011-35537a71|P011|Architecture|Architecture rule 'cycle' satisfied for 'App'|App|True|3|cycle|Project|App|App||",
        "O-P011-295d3267|P011|Architecture|Architecture rule 'fan-out' satisfied for 'imp-1'|imp-1|False|1|fan-out|Symbol|imp-1|App|App/Caller.cs|Caller.Use",
        "C|C-CALLER_POTENTIALLY_TRUNCATED-bcfafe27|CALLER_POTENTIALLY_TRUNCATED|blocking||Caller collection reached a result limit",
        string.Empty
    ]);

    private static string Canonicalize(ProofPlan plan)
    {
        var builder = new StringBuilder();
        foreach (var obligation in plan.Obligations)
        {
            builder.Append(obligation.Id).Append('|')
                .Append(obligation.RuleId).Append('|')
                .Append(obligation.Kind).Append('|')
                .Append(obligation.Claim).Append('|')
                .Append(obligation.SubjectId).Append('|')
                .Append(obligation.Required).Append('|')
                .Append(obligation.RiskWeight).Append('|')
                .Append(string.Join(',', obligation.Reasons)).Append('|')
                .Append(obligation.Subject?.Kind).Append('|')
                .Append(obligation.Subject?.Id).Append('|')
                .Append(obligation.Subject?.Project).Append('|')
                .Append(obligation.Subject?.File).Append('|')
                .Append(obligation.Subject?.DisplayName)
                .AppendLine();
        }

        foreach (var constraint in plan.Constraints ?? [])
        {
            builder.Append("C|")
                .Append(constraint.Id).Append('|')
                .Append(constraint.Code).Append('|')
                .Append(constraint.Severity).Append('|')
                .Append(constraint.Subject).Append('|')
                .Append(constraint.Message)
                .AppendLine();
        }

        return builder.ToString();
    }

    private static ProofPolicy BuildPolicy() => new(
        TestMappingProjects: ["App"],
        TestMaps:
        [
            new TestMapEntry("App.Legacy.Do", ["App.Tests.LegacyTests.Do_still_calls"]),
            new TestMapEntry("App.Service.Compute", ["App.Tests.ServiceTests.Compute_ok"])
        ],
        PublicApiCompatibilityRequired: true,
        InternalConsumerCompatibilityRequired: true,
        AppContractRequired: true,
        Architecture: ArchitecturePolicyMode.Required,
        PathRules: [new PathRule("docs/**", PathPolicy.ManualReviewEffect)]);

    private static ChangeImpact BuildImpact() => new(
        "base",
        "head",
        [new LineSpan("App/Legacy.cs", 1, 10), new LineSpan("docs/design.md", 5, 6)],
        [
            new ChangedSymbolRef("pub-1", "App", "App/Legacy.cs", "Legacy.Do", 1, 10, IsPublic: true, IsTest: false),
            new ChangedSymbolRef("priv-1", "App", "App/Service.cs", "Service.Compute", 1, 10, IsPublic: false, IsTest: false),
            new ChangedSymbolRef("dup-1", "App", "App/Other.cs", "Other.Run", 1, 5, IsPublic: false, IsTest: false),
            new ChangedSymbolRef("dup-1", "App", "App/Other.cs", "Other.Run", 1, 5, IsPublic: false, IsTest: false)
        ],
        [
            new ImpactedSymbolRef("imp-1", "App", "App/Caller.cs", "Caller.Use", "pub-1", 1, IsTest: false, "Semantic", 1.0),
            new ImpactedSymbolRef("test-imp-1", "App.Tests", "tests/App.Tests/LegacyTests.cs", "LegacyTests.Do", "priv-1", 1, IsTest: true, "Semantic", 1.0),
            new ImpactedSymbolRef("app-route", "App", "App/Endpoints.cs", "Endpoints.Route", "pub-1", 1, IsTest: false, "Semantic", 1.0),
            new ImpactedSymbolRef("heur-1", "App", "App/Guess.cs", "Guess.Thing", "priv-1", 2, IsTest: false, "Heuristic", 0.4)
        ],
        ["App", "Consumer"],
        ["caller-1", "test-caller-1"],
        "complete",
        HasHeuristicEdges: false,
        Relations:
        [
            new ImpactRelation("pub-1", "app-route", 1, "RoutesTo", "Semantic", 1.0),
            new ImpactRelation("priv-1", "test-imp-1", 1, "Calls", "Semantic", 1.0),
            new ImpactRelation("priv-1", "heur-1", 2, "UsesType", "Heuristic", 0.4)
        ],
        Callers:
        [
            new CallerRelation("pub-1", "test-caller-1", "App.Tests", "tests/App.Tests/LegacyTests.cs", "Calls", 1.0, IsTest: true),
            new CallerRelation("pub-1", "caller-1", "Consumer", "Consumer/UseLegacy.cs", "Calls", 1.0, IsTest: false),
            new CallerRelation("priv-1", "test-caller-1", "App.Tests", "tests/App.Tests/ServiceTests.cs", "Calls", 1.0, IsTest: true)
        ],
        Completeness: new ImpactCompleteness(
            CoverageState.Complete,
            CoverageState.PotentiallyTruncated,
            2,
            500,
            50,
            ImpactPotentiallyTruncated: false,
            CallerPotentiallyTruncated: true,
            UnknownSpanCount: 0,
            HeuristicRelationCount: 1,
            MinimumConfidence: 0.4),
        FileDeltas:
        [
            new FileDelta(FileChangeKind.Deleted, "App/Legacy.cs", null, [], []),
            new FileDelta(FileChangeKind.Deleted, "App/Untraced.cs", null, [], []),
            new FileDelta(FileChangeKind.BinaryModified, "assets/logo.png", "assets/logo.png", [], []),
            new FileDelta(FileChangeKind.SubmoduleChanged, "vendor/lib", "vendor/lib", [], []),
            new FileDelta(FileChangeKind.Modified, "docs/design.md", "docs/design.md", [], [])
        ],
        SourceDigest: "src-digest",
        DeletionPathsResolved: ["App/Legacy.cs"],
        ArchitectureViolations:
        [
            new ArchitectureViolationRef("cycle", "cycle App -> Consumer", "App", "App"),
            new ArchitectureViolationRef("fan-out", "fan-out exceeded", "imp-1", "App", "App/Caller.cs", "Caller.Use")
        ],
        ArchitectureRulesPresent: true);
}
