namespace Proof.Core;

/// <summary>
/// 정규 증명 제공자 id. 한곳에 두어 서명자, 검증자,
/// 이후의 제공자 레지스트리가 와이어 값에 합의하게 한다.
/// </summary>
public static class AttestationProviders
{
    /// <summary>서명이 없다. 봉투는 신원 클레임만 기록한다.</summary>
    public const string None = "none";

    /// <summary>도메인이 분리된 statement 페이로드에 대한 HMAC-SHA256.</summary>
    public const string HmacSha256 = "hmac-sha256";

    public static bool IsSupported(string? provider)
        => string.Equals(provider, None, StringComparison.OrdinalIgnoreCase)
           || string.Equals(provider, HmacSha256, StringComparison.OrdinalIgnoreCase);
}
