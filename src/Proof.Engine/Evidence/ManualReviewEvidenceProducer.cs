using System.Text.Json;
using Proof.Core;

namespace Proof.Engine;

/// <summary>
/// 서명된 리뷰 페이로드가 뒷받침하는 P009 의무에 ManualReview 증거를
/// 낸다. 파일은 <c>.proof/reviews/*.json</c>이다. schema v2 리뷰는
/// <c>schemaVersion: 2</c>와 HMAC에 묶인 <c>reviewer</c>를 담는다
/// (<c>proofmark:review:v2|digest|subject|reviewer</c>). 레거시 v1 리뷰는
/// <c>statementDigest + "|" + subjectId</c>에 서명하고 reviewer가 없다. 서명 없음,
/// 손상, 오래됨, reviewer 불일치, 허용되지 않은 스키마의 리뷰는
/// 아무것도 내지 않는다. 자유 형식 리뷰 텍스트는 증거가 결코 아니다.
/// </summary>
public sealed class ManualReviewEvidenceProducer : IManualReviewEvidenceProducer
{
    private static readonly int[] DefaultAcceptedSchemas =
        [ReviewSignature.LegacySchemaVersion, ReviewSignature.ReviewerBoundSchemaVersion];

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IReadOnlyList<int> _acceptedSchemas;

    /// <summary>
    /// <paramref name="acceptedSchemas"/>는 증거를 낼 수 있는 리뷰 파일
    /// 스키마 버전을 제한한다. null 또는 빈 목록은 레거시 v1 형식과
    /// reviewer-bound v2 형식을 모두 받아들인다(마이그레이션 기본값).
    /// v2를 요구하려면 <c>policy.manualReview.acceptedSchemas: [2]</c>로 설정한다.
    /// </summary>
    public ManualReviewEvidenceProducer(IReadOnlyCollection<int>? acceptedSchemas = null)
        => _acceptedSchemas = acceptedSchemas is { Count: > 0 }
            ? [.. acceptedSchemas]
            : DefaultAcceptedSchemas;

    public Task<IReadOnlyList<ProofEvidence>> AnalyzeAsync(
        ChangeRequest request,
        ProofPlan proofPlan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(proofPlan);
        cancellationToken.ThrowIfCancellationRequested();

        var obligations = proofPlan.Obligations.Where(item => item.Kind == ObligationKind.ManualReview).ToArray();
        if (obligations.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<ProofEvidence>>([]);
        }

        var digest = request.SourceDigest ?? proofPlan.SourceDigest;
        if (string.IsNullOrWhiteSpace(digest))
        {
            return Task.FromResult<IReadOnlyList<ProofEvidence>>([]);
        }

        var reviews = ReadReviews(request.WorkspaceRoot);
        var evidence = new List<ProofEvidence>();
        var index = 0;
        foreach (var obligation in obligations)
        {
            var reviewed = reviews.Any(review =>
                string.Equals(review.SubjectId, obligation.SubjectId, StringComparison.Ordinal)
                && string.Equals(review.StatementDigest, digest, StringComparison.Ordinal)
                && SignatureMatches(digest, obligation.SubjectId, review));
            if (!reviewed)
            {
                continue;
            }

            index++;
            evidence.Add(new ProofEvidence(
                $"MR{index}",
                EvidenceKind.ManualReview,
                obligation.SubjectId,
                EvidenceStatus.Pass,
                new EvidenceProvenance(
                    "manual-review",
                    CheckId: EvidenceCheckIds.ManualReview,
                    SourceDigest: digest),
                new EvidenceScope(
                    ScopeMode.Exact,
                    CommandTarget: null,
                    SubjectRefs:
                    [
                        new EvidenceSubjectRef(
                            SubjectKind.File,
                            obligation.SubjectId,
                            File: obligation.SubjectId,
                            DisplayName: obligation.SubjectId)
                    ])));
        }

        return Task.FromResult<IReadOnlyList<ProofEvidence>>(evidence);
    }

    private bool SignatureMatches(string statementDigest, string subjectId, ReviewPayload review)
    {
        var schema = review.SchemaVersion ?? ReviewSignature.LegacySchemaVersion;
        if (!_acceptedSchemas.Contains(schema))
        {
            return false;
        }

        return schema >= ReviewSignature.ReviewerBoundSchemaVersion
            ? ReviewSignature.IsValidV2(statementDigest, subjectId, review.Reviewer, review.Signature)
            : ReviewSignature.IsValid(statementDigest, subjectId, review.Signature);
    }

    private static IReadOnlyList<ReviewPayload> ReadReviews(string workspaceRoot)
    {
        var directory = Path.Combine(workspaceRoot, ".proof", "reviews");
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var reviews = new List<ReviewPayload>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(value => value, StringComparer.Ordinal))
        {
            try
            {
                var payload = JsonSerializer.Deserialize<ReviewPayload>(File.ReadAllText(path), Options);
                if (payload is not null)
                {
                    reviews.Add(payload);
                }
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                // 손상된 리뷰 파일은 아무것도 기여하지 않으며, 검증 전체를
                // 결코 실패시키지 않는다.
            }
        }

        return reviews;
    }

    internal sealed record ReviewPayload(
        string? SubjectId,
        string? StatementDigest,
        string? Signature,
        string? Reviewer,
        int? SchemaVersion = null);
}
