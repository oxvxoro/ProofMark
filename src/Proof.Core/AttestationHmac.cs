using System.Security.Cryptography;
using System.Text;

namespace Proof.Core;

/// <summary>
/// 증명 서명자와 수동 리뷰 검증기가 공유하는 HMAC-SHA256 계산의
/// 유일한 소유자. 키는 환경에서 읽으며, 어떤 봉투나
/// 인증서에도 결코 직렬화되지 않는다.
/// </summary>
public static class AttestationHmac
{
    public const string KeyEnvironmentVariable = "PROOF_ATTESTATION_HMAC_KEY";

    // 도메인이 분리된 페이로드. 도메인 태그는 증명 서명이
    // 리뷰 서명으로 검증되는 일을 결코 허용하지 않는다(그 반대도). 둘은
    // 키 하나를 공유한다.
    public static string AttestationPayload(string statementDigest)
        => "proofmark:attestation:v2|" + statementDigest;

    // 레거시(attestation schema v1) 페이로드. 이미 발급된 v1
    // 봉투가 마이그레이션 중에도 검증되게만 둔다. 새 서명은 항상 v2다.
    public static string AttestationPayloadV1(string statementDigest)
        => "proofmark:attestation:v1|" + statementDigest;

    // 레거시(review schema v1) 페이로드. 다이제스트는 리뷰를
    // 스냅샷에 묶고, 주체 id는 의무 하나에 묶는다. 이미 작성된 v1
    // 리뷰를 검증하기 위해서만 둔다.
    public static string ReviewPayload(string statementDigest, string subjectId)
        => statementDigest + "|" + subjectId;

    // review schema v2 페이로드는 리뷰어 신원도 묶는다. 그래서 서명이
    // 다른 리뷰어의 승인으로 결코 재사용될 수 없다.
    public static string ReviewPayloadV2(string sourceDigest, string subjectId, string reviewer)
        => string.Join("|", "proofmark:review:v2", sourceDigest, subjectId, reviewer);

    // 정책 예외 페이로드. 증명 및 리뷰와 도메인이 분리되어
    // 예외 서명이 둘 중 어느 쪽으로도 결코 재사용될 수 없다.
    public static string ExceptionPayload(
        string ruleId,
        string subjectId,
        string sourceDigest,
        string owner,
        string reason,
        string ticket,
        DateTimeOffset? expiresAt,
        string signer)
        => string.Join(
            "|",
            "proofmark:exception:v1",
            ruleId,
            subjectId,
            sourceDigest,
            owner,
            reason,
            ticket,
            expiresAt?.ToUniversalTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            signer);

    public static string? ComputeHex(string payload)
    {
        var key = Environment.GetEnvironmentVariable(KeyEnvironmentVariable);
        return string.IsNullOrWhiteSpace(key) ? null : ComputeHex(key, payload);
    }

    public static string ComputeHex(string key, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}