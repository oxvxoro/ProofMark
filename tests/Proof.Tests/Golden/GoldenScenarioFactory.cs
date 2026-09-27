using Proof.Core;
using Proof.Engine;

namespace Proof.Tests.Golden;

/// <summary>
/// 모든 플래너 규칙, 두 바인더 관계(direct와
/// supporting), 오래된 소스 거절 경로를 다루는 풍부하고 결정적인
/// 시나리오 하나. 골든 테스트와 PR-03/PR-10/PR-13 리팩터는 출력을
/// 이 픽스처에 고정하므로, 의미 변화는 뜻밖의 결과가 아니라 diff로 나타난다.
/// </summary>
internal static class GoldenScenarioFactory
{
    public static ProofPolicy Policy() => new(
        PublicApiCompatibilityRequired: true,
        InternalConsumerCompatibilityRequired: true,
        TestMappingRequired: true,
        StaticAnalysisRequired: true,
        AppContractRequired: true,
        Architecture: ArchitecturePolicyMode.Required,
        TestMappingProjects: ["App"],
        TestMaps:
        [
            new TestMapEntry("App.Legacy.Do", ["App.Tests.LegacyTests.Do_still_calls"])
        ],
        PathRules: [new PathRule("docs/**", PathPolicy.ManualReviewEffect)]);

    public static ChangeImpact Impact() => new(
        "base",
        "head",
        [new LineSpan("App/Legacy.cs", 1, 10), new LineSpan("docs/design.md", 5, 6)],
        [
            new ChangedSymbolRef("pub-1", "App", "App/Legacy.cs", "Legacy.Do", 1, 10, IsPublic: true, IsTest: false),
            new ChangedSymbolRef("priv-1", "App", "App/Service.cs", "Service.Compute", 1, 10, IsPublic: false, IsTest: false)
        ],
        [
            new ImpactedSymbolRef("test-imp-1", "App.Tests", "tests/App.Tests/LegacyTests.cs", "LegacyTests.Do", "pub-1", 1, IsTest: true, "Semantic", 1.0),
            new ImpactedSymbolRef("app-route", "App", "App/Endpoints.cs", "Endpoints.Route", "pub-1", 1, IsTest: false, "Semantic", 1.0),
            new ImpactedSymbolRef("consumer-imp", "Consumer", "Consumer/UseLegacy.cs", "UseLegacy.Call", "pub-1", 1, IsTest: false, "Semantic", 1.0)
        ],
        ["App", "Consumer"],
        ["test-caller-1", "caller-1"],
        "complete",
        HasHeuristicEdges: false,
        Relations:
        [
            new ImpactRelation("pub-1", "app-route", 1, "RoutesTo", "Semantic", 1.0),
            new ImpactRelation("pub-1", "consumer-imp", 1, "Calls", "Semantic", 1.0)
        ],
        Callers:
        [
            new CallerRelation("pub-1", "test-caller-1", "App.Tests", "tests/App.Tests/LegacyTests.cs", "Calls", 1.0, IsTest: true),
            new CallerRelation("pub-1", "caller-1", "Consumer", "Consumer/UseLegacy.cs", "Calls", 1.0, IsTest: false)
        ],
        Completeness: new ImpactCompleteness(
            CoverageState.Complete,
            CoverageState.Complete,
            2,
            500,
            50,
            ImpactPotentiallyTruncated: false,
            CallerPotentiallyTruncated: false,
            UnknownSpanCount: 0,
            HeuristicRelationCount: 0,
            MinimumConfidence: 1.0),
        FileDeltas:
        [
            new FileDelta(FileChangeKind.Modified, "docs/design.md", "docs/design.md", [], []),
            new FileDelta(FileChangeKind.Deleted, "App/Untraced.cs", null, [], [])
        ],
        SourceDigest: "golden-digest",
        DeletionPathsResolved: [],
        ArchitectureViolations:
        [
            new ArchitectureViolationRef("cycle", "cycle App -> Consumer", "App", "App")
        ],
        ArchitectureRulesPresent: true);

    public static ProofPlan Plan()
    {
        var impact = Impact();
        return new DeterministicProofPlanner().Plan(impact, Policy()) with
        {
            SourceDigest = impact.SourceDigest
        };
    }

    public static IReadOnlyList<ProofEvidence> Evidence() =>
    [
        // App 프로젝트에 대한 직접 빌드(공개 API 호환성
        // 의무에 대한 보조 증거이기도 하다).
        Evidence("E-build-app", EvidenceKind.Build, EvidenceStatus.Pass, "build", "build", "golden-digest", "src/App/App.csproj"),
        // 매핑된 테스트 의무에 대한 직접 테스트 케이스.
        Evidence("E-test-mapped", EvidenceKind.TestCase, EvidenceStatus.Pass, "unit", "unit", "golden-digest",
            subjectRefs: [new EvidenceSubjectRef(SubjectKind.Test, "App.Tests.LegacyTests.Do_still_calls", FullyQualifiedName: "App.Tests.LegacyTests.Do_still_calls")]),
        // manual-review 경로에 대한 직접 서명된 수동 리뷰.
        Evidence("E-manual", EvidenceKind.ManualReview, EvidenceStatus.Pass, "manual", "manual", "golden-digest",
            subjectRefs: [new EvidenceSubjectRef(SubjectKind.File, "docs/design.md")]),
        // 오래된 소스 귀속 증거: 항상 거절되어야 하며, 절대 direct가 되면 안 된다.
        Evidence("E-stale", EvidenceKind.TestCase, EvidenceStatus.Pass, "stale", "unit", "other-digest",
            subjectRefs: [new EvidenceSubjectRef(SubjectKind.Test, "App.Tests.LegacyTests.Do_still_calls", FullyQualifiedName: "App.Tests.LegacyTests.Do_still_calls")])
    ];

    public static ChangeCertificate BuildCertificate()
    {
        var impact = Impact();
        var plan = Plan();
        var evidence = Evidence();
        var bound = new EvidenceBinder().Bind(plan, evidence);
        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, bound);
        return new ChangeCertificateBuilder().Build(impact, plan, bound, evaluation);
    }

    /// <summary>순서 불변 속성을 위해 변경된 심볼을 결정적으로 재정렬한다.</summary>
    public static ChangeImpact WithChangedSymbolOrder(ChangeImpact impact, int seed)
    {
        var random = new Random(seed);
        return impact with
        {
            ChangedSymbols = impact.ChangedSymbols.OrderBy(_ => random.Next()).ToArray()
        };
    }

    /// <summary>순서 불변 속성을 위해 영향받은 심볼을 결정적으로 재정렬한다.</summary>
    public static ChangeImpact WithImpactedSymbolOrder(ChangeImpact impact, int seed)
    {
        var random = new Random(seed);
        return impact with
        {
            ImpactedSymbols = impact.ImpactedSymbols.OrderBy(_ => random.Next()).ToArray()
        };
    }

    private static ProofEvidence Evidence(
        string id,
        EvidenceKind kind,
        EvidenceStatus status,
        string subject,
        string? checkId,
        string? sourceDigest,
        string? commandTarget = null,
        IReadOnlyList<EvidenceSubjectRef>? subjectRefs = null)
        => new(
            id,
            kind,
            subject,
            status,
            new EvidenceProvenance("distill", CheckId: checkId, SourceDigest: sourceDigest),
            commandTarget is null && subjectRefs is null
                ? null
                : new EvidenceScope(
                    subjectRefs is { Count: > 0 } ? ScopeMode.Exact : ScopeMode.Contains,
                    null,
                    commandTarget,
                    subjectRefs));
}
