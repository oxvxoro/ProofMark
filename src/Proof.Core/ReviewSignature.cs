using System.Security.Cryptography;

namespace Proof.Core;

/// <summary>
/// 증거 생산자(엔진 경로)와 `review list` CLI(작성 경로)가
/// 공유하는 수동 리뷰 서명 검증. 둘은 유효한 리뷰가
/// 무엇인지에 대해 결코 어긋날 수 없다.
/// </summary>
public static class ReviewSignature
{
    public const int LegacySchemaVersion = 1;

    public const int ReviewerBoundSchemaVersion = 2;

    /// <summary>레거시(schema v1) 리뷰 서명을 검증한다.</summary>
    public static bool IsValid(string statementDigest, string subjectId, string? signature)
        => Verify(AttestationHmac.ReviewPayload(statementDigest, subjectId), signature);

    /// <summary>
    /// schema v2 리뷰 서명을 검증한다. reviewer 값은 리뷰 파일에
    /// 기록된 값이어야 한다. 바꾸면 서명이 무효가 된다.
    /// </summary>
    public static bool IsValidV2(
        string sourceDigest,
        string subjectId,
        string? reviewer,
        string? signature)
        => Verify(AttestationHmac.ReviewPayloadV2(sourceDigest, subjectId, reviewer ?? string.Empty), signature);

    private static bool Verify(string payload, string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var expected = AttestationHmac.ComputeHex(payload);
        if (expected is null)
        {
            return false;
        }

        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected),
                Convert.FromHexString(signature));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
