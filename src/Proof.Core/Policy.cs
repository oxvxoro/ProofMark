namespace Proof.Core;

using System.Collections.Frozen;

public enum UncertaintyDisposition
{
    Advisory,
    Blocking
}

public sealed record ProofPolicy(
    UncertaintyDisposition HeuristicImpact = UncertaintyDisposition.Advisory,
    UncertaintyDisposition LocationUnknown = UncertaintyDisposition.Blocking,
    UncertaintyDisposition ImpactTruncated = UncertaintyDisposition.Blocking,
    UncertaintyDisposition CallerTruncated = UncertaintyDisposition.Blocking,
    bool PublicApiCompatibilityRequired = true,
    bool InternalConsumerCompatibilityRequired = true,
    bool TestMappingRequired = true,
    bool StaticAnalysisRequired = false,
    bool AppContractRequired = false,
    ArchitecturePolicyMode Architecture = ArchitecturePolicyMode.Off,
    double MinConfidence = 0.75,
    IReadOnlyList<TestMapEntry>? TestMaps = null,
    IReadOnlyList<PathRule>? PathRules = null,
    IReadOnlyList<string>? FailOnUncertainCodes = null,
    IReadOnlyList<string>? TestMappingProjects = null,
    IReadOnlyList<int>? ManualReviewAcceptedSchemas = null);

public sealed record TestMapEntry(string Symbol, IReadOnlyList<string> Tests);

// 명시적 테스트 맵의 공유 신원 일치. 바인더의 정확한
// TestCase 규칙을 따른다. 서명을 자른 동등성과 세그먼트 경계 접미사뿐이다.
// 짧은 메서드 이름만 있는 키는 접미사로 결코 일치하지 않는다.
public static class TestMapMatching
{
    public static bool SymbolMatches(string? subjectId, string? displayName, string symbol)
        => SubjectIdentityMatcher.SymbolMatches(subjectId, displayName, symbol);

    public static string TrimSignature(string value)
        => SubjectIdentityMatcher.TrimSignature(value);
}

// 필수 P005 프로젝트 범위. proof.yml이 policy.testMappingProjects를 나열하면,
// CodeMap 프로젝트가 그 목록에 있는 변경 심볼만 필수
// P005를 가진다. 범위 밖 심볼은 보이는 조언 P005를 유지한다. 일치는
// 정확한 프로젝트 이름이다(대소문자 무시). 글롭도 경로 조각도 없고,
// 부분 이름("Proof")은 "Proof.Engine"과 결코 일치하지 않는다. null/빈 목록은
// 전역 필수 기본값을 유지한다. 목록이 비어 있지 않은데 프로젝트가 비었거나
// 알 수 없으면 범위 밖이다(이름 없는 프로젝트는 이름이 있다고 증명할 수 없다).
public static class TestMappingScope
{
    public static bool IsRequired(string? project, ProofPolicy policy)
    {
        if (!policy.TestMappingRequired)
        {
            return false;
        }

        var projects = policy.TestMappingProjects;
        if (projects is null || projects.Count == 0)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(project))
        {
            return false;
        }

        foreach (var entry in projects)
        {
            if (string.Equals(entry, project, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}


public sealed record PathRule(string Match, string Effect);

public enum PathRuleEffect
{
    Ignore,
    ManualReview
}

public static class PathPolicy
{
    public const string IgnoreEffect = "ignore";
    public const string ManualReviewEffect = "manual-review";

    public static bool IsIgnored(string repoRelativePath, IReadOnlyList<PathRule>? rules)
        => MatchesEffect(repoRelativePath, rules, IgnoreEffect);

    // 수동 리뷰 경로는 변경 집합에 남는다(무시된 파일처럼 결코 빠지지 않는다).
    // 서명된 리뷰만 닫을 수 있는
    // P009 의무를 만든다.
    public static bool IsManualReview(string repoRelativePath, IReadOnlyList<PathRule>? rules)
        => MatchesEffect(repoRelativePath, rules, ManualReviewEffect);

    private static bool MatchesEffect(string repoRelativePath, IReadOnlyList<PathRule>? rules, string effect)
    {
        if (rules is null || rules.Count == 0)
        {
            return false;
        }

        var normalized = (repoRelativePath ?? string.Empty).Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        foreach (var rule in rules)
        {
            if (rule is not { Match: not null } || !string.Equals(rule.Effect, effect, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Matches(normalized, rule.Match.Replace('\\', '/').TrimStart('/')))
            {
                return true;
            }
        }

        return false;
    }

    // 글롭 부분집합. `**`는 경로 세그먼트를 몇 개든 가로지른다(0개 포함).
    // `*`는 한 세그먼트 안에서 '/'를 제외한 어떤 문자든 가로지른다.
    private static bool Matches(string path, string pattern)
    {
        var regex = GlobToRegex(pattern);
        return regex.IsMatch(path);
    }

    private static System.Text.RegularExpressions.Regex GlobToRegex(string pattern)
    {
        var builder = new System.Text.StringBuilder("^");
        for (var index = 0; index < pattern.Length; index++)
        {
            var current = pattern[index];
            if (current == '*')
            {
                if (index + 1 < pattern.Length && pattern[index + 1] == '*')
                {
                    // `**`는 주변 슬래시를 포함해 어떤 세그먼트든 소비한다.
                    builder.Append("(?:[^/]*/)*[^/]*");
                    if (index + 2 < pattern.Length && pattern[index + 2] == '/')
                    {
                        index += 2;
                    }
                    else
                    {
                        index++;
                    }
                }
                else
                {
                    builder.Append("[^/]*");
                }
            }
            else if (current == '?')
            {
                builder.Append("[^/]");
            }
            else
            {
                builder.Append(System.Text.RegularExpressions.Regex.Escape(current.ToString()));
            }
        }

        builder.Append('$');
        return new System.Text.RegularExpressions.Regex(builder.ToString(), System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}

public static class ProofReasonCodes
{
    public const string ChangeNonemptyPlanEmpty = "CHANGE_NONEMPTY_PLAN_EMPTY";
    public const string ChangeCaptureUntrackedFailed = "CHANGE_CAPTURE_UNTRACKED_FAILED";
    public const string ImpactLocationUnknown = "IMPACT_LOCATION_UNKNOWN";
    public const string ImpactPotentiallyTruncated = "IMPACT_POTENTIALLY_TRUNCATED";
    public const string CallerPotentiallyTruncated = "CALLER_POTENTIALLY_TRUNCATED";
    public const string HeuristicEdgeBlocking = "HEURISTIC_EDGE_BLOCKING";
    public const string EvidenceScopeMismatch = "EVIDENCE_SCOPE_MISMATCH";
    public const string EvidenceKindMismatch = "EVIDENCE_KIND_MISMATCH";
    public const string EvidenceStaleSource = "EVIDENCE_STALE_SOURCE";
    public const string EvidenceSourceIdentityMissing = "EVIDENCE_SOURCE_IDENTITY_MISSING";
    public const string RequiredEvidenceCapabilityMissing = "REQUIRED_EVIDENCE_CAPABILITY_MISSING";
    public const string ChangeDeletionAnalysisUnavailable = "CHANGE_DELETION_ANALYSIS_UNAVAILABLE";
    public const string ChangeBinaryAnalysisUnavailable = "CHANGE_BINARY_ANALYSIS_UNAVAILABLE";
    public const string ChangeSubmoduleAnalysisUnavailable = "CHANGE_SUBMODULE_ANALYSIS_UNAVAILABLE";
    public const string ChangeUnsupportedKind = "CHANGE_UNSUPPORTED_KIND";
    public const string ChangeCaptureContentHashFailed = "CHANGE_CAPTURE_CONTENT_HASH_FAILED";
    public const string ChangeStructuredDeltaFailed = "CHANGE_STRUCTURED_DELTA_FAILED";
    public const string SourceFreshnessDrift = "SOURCE_FRESHNESS_DRIFT";
    public const string EvidenceTooWeak = "EVIDENCE_TOO_WEAK";
    public const string ManualReviewRequired = "MANUAL_REVIEW_REQUIRED";
    public const string RequiredEvidenceMissing = "REQUIRED_EVIDENCE_MISSING";
    public const string RequiredEvidenceFailed = "REQUIRED_EVIDENCE_FAILED";
    public const string DistillConfigNotFound = "DISTILL_CONFIG_NOT_FOUND";
    public const string BaseRefNotFound = "BASE_REF_NOT_FOUND";
    public const string FileWideFallback = "CHANGE_FILE_WIDE_FALLBACK";
    public const string NoChange = "NO_CHANGE";

    private static readonly FrozenSet<string> KnownCodes = new[]
    {
        ChangeNonemptyPlanEmpty,
        ChangeCaptureUntrackedFailed,
        ImpactLocationUnknown,
        ImpactPotentiallyTruncated,
        CallerPotentiallyTruncated,
        HeuristicEdgeBlocking,
        EvidenceScopeMismatch,
        EvidenceKindMismatch,
        EvidenceStaleSource,
        EvidenceSourceIdentityMissing,
        RequiredEvidenceCapabilityMissing,
        ChangeDeletionAnalysisUnavailable,
        ChangeBinaryAnalysisUnavailable,
        ChangeSubmoduleAnalysisUnavailable,
        ChangeUnsupportedKind,
        ChangeCaptureContentHashFailed,
        ChangeStructuredDeltaFailed,
        SourceFreshnessDrift,
        EvidenceTooWeak,
        ManualReviewRequired,
        RequiredEvidenceMissing,
        RequiredEvidenceFailed,
        DistillConfigNotFound,
        BaseRefNotFound,
        FileWideFallback,
        NoChange
    }.ToFrozenSet(StringComparer.Ordinal);

    // 엔진 자신의 SCREAMING_SNAKE 이유 코드에만 참이다. 의무
    // 이유도 플래너 산문(예: "no mapped test relation
    // found")을 담는데, CI merge-block 코드를 해석할 때 이유 코드로
    // 다루어서는 안 된다. 해석기는 REQUIRED_EVIDENCE_MISSING으로
    // 돌아간다. 이 클래스에 더한 코드는 자동으로 알려진 코드다.
    public static bool IsKnown(string? code) => code is not null && KnownCodes.Contains(code);
}
