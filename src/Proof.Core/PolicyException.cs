using System.Security.Cryptography;

namespace Proof.Core;

/// <summary>
/// 서명되고 기간이 한정된 정책 예외. 예외는 거버넌스
/// 산출물일 뿐이다. 의무 상태나 인증서 판정을
/// 결코 바꾸지 않는다. Exception != Proven.
/// </summary>
public sealed record PolicyException(
    string RuleId,
    string SubjectId,
    string Owner,
    string Reason,
    string? Ticket = null,
    string? SourceDigest = null,
    DateTimeOffset? ExpiresAt = null,
    string? Signer = null,
    string? Signature = null);

public enum PolicyExceptionState
{
    Valid,
    Unsigned,
    InvalidSignature,
    Expired,

    // 예외에 서명은 있으나 HMAC 키를 쓸 수 없다. 그래서
    // 유효성을 결정할 수 없다(결코 무효로 조용히 보고하지 않는다).
    Unverifiable
}

/// <summary>
/// 정책 예외에 대한 HMAC 서명. 도메인이 분리된 페이로드로 공유
/// <see cref="AttestationHmac"/> 키 하나를 재사용한다. 그래서 예외
/// 서명이 증명이나 수동 리뷰로 결코 통과할 수 없다.
/// </summary>
public static class PolicyExceptionSignature
{
    public const string AllSubjects = "*";

    public static string Payload(PolicyException exception) => AttestationHmac.ExceptionPayload(
        exception.RuleId,
        exception.SubjectId,
        exception.SourceDigest ?? string.Empty,
        exception.Owner,
        exception.Reason,
        exception.Ticket ?? string.Empty,
        exception.ExpiresAt,
        exception.Signer ?? string.Empty);

    public static string? Sign(PolicyException exception) => AttestationHmac.ComputeHex(Payload(exception));

    public static bool IsValid(PolicyException exception)
    {
        if (string.IsNullOrWhiteSpace(exception.Signature))
        {
            return false;
        }

        var expected = AttestationHmac.ComputeHex(Payload(exception));
        if (expected is null)
        {
            return false;
        }

        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected),
                Convert.FromHexString(exception.Signature));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>
/// 서명된 예외에 대한 결정적 거버넌스. "이 열린 필수 의무를
/// 이 소스 다이제스트에 대해 예외할 수 있는가?"에 답한다. 증명
/// 판정이나 인증서 statement는 건드리지 않는다.
/// </summary>
public static class PolicyExceptionGovernance
{
    public static PolicyExceptionState EvaluateState(PolicyException exception, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(exception.Signature))
        {
            return PolicyExceptionState.Unsigned;
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable)))
        {
            return PolicyExceptionState.Unverifiable;
        }

        if (!PolicyExceptionSignature.IsValid(exception))
        {
            return PolicyExceptionState.InvalidSignature;
        }

        return exception.ExpiresAt is { } expires && expires <= now
            ? PolicyExceptionState.Expired
            : PolicyExceptionState.Valid;
    }

    public static bool Covers(
        PolicyException exception,
        string ruleId,
        string subjectId,
        string? sourceDigest)
    {
        if (!string.Equals(exception.RuleId, ruleId, StringComparison.Ordinal))
        {
            return false;
        }

        var subjectMatches = string.IsNullOrWhiteSpace(exception.SubjectId)
            || string.Equals(exception.SubjectId, PolicyExceptionSignature.AllSubjects, StringComparison.Ordinal)
            || string.Equals(exception.SubjectId, subjectId, StringComparison.Ordinal);
        if (!subjectMatches)
        {
            return false;
        }

        // 소스에 묶인 예외는 자기 스냅샷만 덮는다. 다이제스트가 없는
        // 예외는 규칙/주체 범위이며, 다른 소스를 덮는 것으로
        // 조용히 다루지 않는다.
        if (!string.IsNullOrWhiteSpace(exception.SourceDigest) && !string.IsNullOrWhiteSpace(sourceDigest))
        {
            return string.Equals(exception.SourceDigest, sourceDigest, StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(exception.SourceDigest);
    }
}
