using System.Text;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

/// <summary>
/// 유효, 오래됨, 다이제스트 누락, 허용 목록, 무관한 증거가 섞인
/// 픽스처에 대해 바인더의 정확한 링크 목록(순서와 거절된 링크 포함)을 고정한다.
/// PR-04 인덱싱 리팩터는 어떤 링크도 바꾸면 안 된다.
/// </summary>
public sealed class EvidenceBinderEquivalenceTests
{
    [Fact]
    public void Bind_MixedEvidence_ProducesStableLinkOrder()
    {
        var plan = BuildPlan();
        var evidence = BuildEvidence();
        var verificationPlan = new VerificationPlan(
            [
                new PlannedVerificationCheck("build", "build", "App.csproj"),
                new PlannedVerificationCheck("unit", "test", "App.Tests.csproj"),
                new PlannedVerificationCheck("coverage", "runtimecoverage", "App.csproj"),
                new PlannedVerificationCheck("manual", "manualreview", "review"),
                new PlannedVerificationCheck("static", "analysis", "App.csproj"),
                new PlannedVerificationCheck("arch", "architecture", "architecture-rules"),
                new PlannedVerificationCheck("api", "apicompatibility", "App.csproj")
            ],
            [],
            "quick");

        var bound = new EvidenceBinder().Bind(plan, evidence, verificationPlan);
        var actual = Canonicalize(bound.Links).Replace("\r\n", "\n");

        Assert.Equal(Expected, actual);
    }

    private static string Canonicalize(IReadOnlyList<ObligationEvidenceLink> links)
    {
        var builder = new StringBuilder();
        foreach (var link in links)
        {
            builder.Append(link.ObligationId).Append('|')
                .Append(link.EvidenceId).Append('|')
                .Append(link.Relation).Append('|')
                .Append(link.Strength).Append('|')
                .Append(link.BindingRuleId).Append('|')
                .Append(link.ReasonCode).Append('|')
                .Append(link.Explanation)
                .AppendLine();
        }

        return builder.ToString();
    }

    private static ProofPlan BuildPlan() => new(
        [
            Obligation("O1", ObligationKind.Test, "sym-test", "App.Tests", "App.Tests.OrderServiceTests.Cancel_keeps_order"),
            Obligation("O2", ObligationKind.Build, "App", "App"),
            Obligation("O3", ObligationKind.Compatibility, "public-api", "App"),
            Obligation("O4", ObligationKind.CallerContract, "caller-1", "Consumer"),
            Obligation("O5", ObligationKind.TestMapping, "mapping-1", "App"),
            Obligation("O6", ObligationKind.ManualReview, "docs/design.md", null),
            Obligation("O7", ObligationKind.AppContract, "route-1", "App"),
            Obligation("O8", ObligationKind.Architecture, "architecture-rules", null),
            Obligation("O9", ObligationKind.StaticAnalysis, "App", "App"),
            Obligation("O10", ObligationKind.Uncertainty, "code", null),
            Obligation("O11", ObligationKind.CrossProject, "Consumer", "Consumer")
        ],
        SourceDigest: "digest-a");

    private static IReadOnlyList<ProofEvidence> BuildEvidence() =>
    [
        // E1 App에 대한 유효한 빌드. O2와 일치하고, E14 이후의 (정확한) O11과도? 아니다. App만.
        Evidence("E1", EvidenceKind.Build, EvidenceStatus.Pass, "build", "build", "digest-a", "src/App/App.csproj"),
        // E2 오래된 테스트 케이스: Uncertainty가 아닌 모든 의무에 대해 거절된다.
        Evidence("E2", EvidenceKind.TestCase, EvidenceStatus.Pass, "stale", "unit", "digest-b",
            subjectRefs: [new EvidenceSubjectRef(SubjectKind.Test, "sym-test", FullyQualifiedName: "App.Tests.OrderServiceTests.Cancel_keeps_order")]),
        // E3 다이제스트가 없는 테스트 케이스: Uncertainty가 아닌 모든 의무에 대해 거절된다.
        Evidence("E3", EvidenceKind.TestCase, EvidenceStatus.Pass, "missing", "unit", null),
        // E4 mapping-1에 대한 유효한 런타임 커버리지(O5).
        Evidence("E4", EvidenceKind.RuntimeCoverage, EvidenceStatus.Pass, "mapping-1", "coverage", "digest-a", "src/App/App.csproj"),
        // E5 docs/design.md에 대한 유효한 수동 리뷰.
        Evidence("E5", EvidenceKind.ManualReview, EvidenceStatus.Pass, "manual", "manual", "digest-a",
            subjectRefs: [new EvidenceSubjectRef(SubjectKind.File, "docs/design.md")]),
        // E6 O1과 일치하는 유효한 테스트 케이스.
        Evidence("E6", EvidenceKind.TestCase, EvidenceStatus.Pass, "unit", "unit", "digest-a",
            subjectRefs: [new EvidenceSubjectRef(SubjectKind.Test, "sym-test", FullyQualifiedName: "App.Tests.OrderServiceTests.Cancel_keeps_order")]),
        // E7 App에 대한 유효한 정적 분석(O9).
        Evidence("E7", EvidenceKind.StaticAnalysis, EvidenceStatus.Pass, "App", "static", "digest-a", "src/App/App.csproj"),
        // E8 유효한 아키텍처 증거(O8).
        Evidence("E8", EvidenceKind.Architecture, EvidenceStatus.Pass, "architecture-rules", "arch", "digest-a"),
        // E9 App에 대한 유효한 API 호환성(O3).
        Evidence("E9", EvidenceKind.ApiCompatibility, EvidenceStatus.Pass, "App", "api", "digest-a", "src/App/App.csproj"),
        // E10 Consumer에 대한 정확한 빌드(O4 supporting, O11 direct).
        Evidence("E10", EvidenceKind.Build, EvidenceStatus.Pass, "build", "build", "digest-a", "src/Consumer/Consumer.csproj"),
        // E11 일치하지 않는 유효한 테스트 실행(O1 후보이지만 식별이 일치하지 않음).
        Evidence("E11", EvidenceKind.TestRun, EvidenceStatus.Pass, "unit", "unit", "digest-a",
            scopeSubjects: ["App.Tests.Unrelated.Fact"]),
        // E12 검증 계획 허용 목록에 의해 제외됨.
        Evidence("E12", EvidenceKind.Build, EvidenceStatus.Pass, "build", "unplanned", "digest-a", "src/App/App.csproj"),
        // E13 route-1에 대한 유효한 런타임 커버리지(O7).
        Evidence("E13", EvidenceKind.RuntimeCoverage, EvidenceStatus.Pass, "route-1", "coverage", "digest-a", "src/App/App.csproj")
    ];

    private static readonly string Expected = string.Join('\n',
    [
        "O1|E2|rejected|0|BIND_SOURCE_DIGEST|EVIDENCE_STALE_SOURCE|Evidence source digest does not match the planned snapshot.",
        "O1|E3|rejected|0|BIND_SOURCE_DIGEST|EVIDENCE_SOURCE_IDENTITY_MISSING|Evidence is missing a source digest while the plan requires one.",
        "O1|E6|direct|3|BIND_TEST_CASE|SCOPE_MATCH|Test case identity matches the impacted test obligation.",
        "O2|E1|direct|3|BIND_BUILD_PROJECT|SCOPE_MATCH|Exact project build target covers the obligation.",
        "O2|E2|rejected|0|BIND_SOURCE_DIGEST|EVIDENCE_STALE_SOURCE|Evidence source digest does not match the planned snapshot.",
        "O2|E3|rejected|0|BIND_SOURCE_DIGEST|EVIDENCE_SOURCE_IDENTITY_MISSING|Evidence is missing a source digest while the plan requires one.",
        "O3|E1|supporting|2|BIND_BUILD_SUPPORTING_API|EVIDENCE_TOO_WEAK|Generic build cannot directly prove public API compatibility.",
        "O3|E2|rejected|0|BIND_SOURCE_DIGEST|EVIDENCE_STALE_SOURCE|Evidence source digest does not match the planned snapshot.",
        "O3|E3|rejected|0|BIND_SOURCE_DIGEST|EVIDENCE_SOURCE_IDENTITY_MISSING|Evidence is missing a source digest while the plan requires one.",
        "O3|E9|direct|3|BIND_API_COMPAT|SCOPE_MATCH|API compatibility evidence covers the public API claim.",
        "O3|E10|supporting|2|BIND_BUILD_SUPPORTING_API|EVIDENCE_TOO_WEAK|Generic build cannot directly prove public API compatibility.",
        "O4|E2|rejected|0|BIND_SOURCE_DIGEST|EVIDENCE_STALE_SOURCE|Evidence source digest does not match the planned snapshot.",
        "O4|E3|rejected|0|BIND_SOURCE_DIGEST|EVIDENCE_SOURCE_IDENTITY_MISSING|Evidence is missing a source digest while the plan requires one.",
        "O4|E10|supporting|2|BIND_CALLER_COMPILE|SCOPE_CONTAINS|Caller project compile is supporting caller-contract evidence.",
        "O5|E2|rejected|0|BIND_SOURCE_DIGEST|EVIDENCE_STALE_SOURCE|Evidence source digest does not match the planned snapshot.",
        "O5|E3|rejected|0|BIND_SOURCE_DIGEST|EVIDENCE_SOURCE_IDENTITY_MISSING|Evidence is missing a source digest while the plan requires one.",
        "O5|E4|direct|3|BIND_TEST_MAPPING|SCOPE_MATCH|Subject-matched mapping evidence covers this obligation.",
        "O6|E2|rejected|0|BIND_SOURCE_DIGEST|EVIDENCE_STALE_SOURCE|Evidence source digest does not match the planned snapshot.",
        "O6|E3|rejected|0|BIND_SOURCE_DIGEST|EVIDENCE_SOURCE_IDENTITY_MISSING|Evidence is missing a source digest while the plan requires one.",
        "O6|E5|direct|3|BIND_MANUAL_REVIEW|SCOPE_MATCH|Signed manual review covers this path.",
        "O7|E1|supporting|2|BIND_APP_BUILD_SUPPORTING|EVIDENCE_TOO_WEAK|A project build is supporting evidence only for an app contract.",
        "O7|E2|rejected|0|BIND_SOURCE_DIGEST|EVIDENCE_STALE_SOURCE|Evidence source digest does not match the planned snapshot.",
        "O7|E3|rejected|0|BIND_SOURCE_DIGEST|EVIDENCE_SOURCE_IDENTITY_MISSING|Evidence is missing a source digest while the plan requires one.",
        "O7|E7|supporting|2|BIND_APP_BUILD_SUPPORTING|EVIDENCE_TOO_WEAK|Static analysis is supporting evidence only for an app contract.",
        "O7|E13|direct|3|BIND_APP_COVERAGE|SCOPE_MATCH|Runtime coverage of the app-contract symbol covers this obligation.",
        "O8|E2|rejected|0|BIND_SOURCE_DIGEST|EVIDENCE_STALE_SOURCE|Evidence source digest does not match the planned snapshot.",
        "O8|E3|rejected|0|BIND_SOURCE_DIGEST|EVIDENCE_SOURCE_IDENTITY_MISSING|Evidence is missing a source digest while the plan requires one.",
        "O8|E8|direct|3|BIND_ARCHITECTURE|SCOPE_MATCH|Subject-matched architecture evidence covers this obligation.",
        "O9|E2|rejected|0|BIND_SOURCE_DIGEST|EVIDENCE_STALE_SOURCE|Evidence source digest does not match the planned snapshot.",
        "O9|E3|rejected|0|BIND_SOURCE_DIGEST|EVIDENCE_SOURCE_IDENTITY_MISSING|Evidence is missing a source digest while the plan requires one.",
        "O9|E7|direct|3|BIND_STATIC_ANALYSIS|SCOPE_MATCH|Project-scoped static analysis evidence covers this obligation.",
        "O11|E2|rejected|0|BIND_SOURCE_DIGEST|EVIDENCE_STALE_SOURCE|Evidence source digest does not match the planned snapshot.",
        "O11|E3|rejected|0|BIND_SOURCE_DIGEST|EVIDENCE_SOURCE_IDENTITY_MISSING|Evidence is missing a source digest while the plan requires one.",
        "O11|E10|direct|3|BIND_BUILD_PROJECT|SCOPE_MATCH|Exact project build target covers the obligation.",
        string.Empty
    ]);

    private static ProofObligation Obligation(
        string id,
        ObligationKind kind,
        string subjectId,
        string? project,
        string? displayName = null)
        => new(
            id,
            "P000",
            kind,
            $"claim-{id}",
            subjectId,
            true,
            1,
            ["r"],
            new ProofSubject(
                kind is ObligationKind.Test ? SubjectKind.Test
                    : kind is ObligationKind.ManualReview ? SubjectKind.File
                    : kind is ObligationKind.Architecture ? SubjectKind.Repository
                    : SubjectKind.Symbol,
                subjectId,
                project,
                File: subjectId.Contains('/') ? subjectId : null,
                DisplayName: displayName ?? subjectId));

    private static ProofEvidence Evidence(
        string id,
        EvidenceKind kind,
        EvidenceStatus status,
        string subject,
        string? checkId,
        string? sourceDigest,
        string? commandTarget = null,
        IReadOnlyList<string>? scopeSubjects = null,
        IReadOnlyList<EvidenceSubjectRef>? subjectRefs = null)
        => new(
            id,
            kind,
            subject,
            status,
            new EvidenceProvenance("distill", CheckId: checkId, SourceDigest: sourceDigest),
            commandTarget is null && scopeSubjects is null && subjectRefs is null
                ? null
                : new EvidenceScope(
                    commandTarget is not null
                    && (commandTarget.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                        || commandTarget.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
                        ? ScopeMode.RepositoryWide
                        : subjectRefs is { Count: > 0 } ? ScopeMode.Exact : ScopeMode.Contains,
                    scopeSubjects,
                    commandTarget,
                    subjectRefs));
}
