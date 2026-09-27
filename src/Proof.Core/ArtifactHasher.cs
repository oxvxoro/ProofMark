namespace Proof.Core;

/// <summary>
/// 증거 출처와 인증서 산출물 매니페스트가 쓰는 공유 산출물 해시.
/// 없거나 읽을 수 없는 산출물은 null을 내어, 매니페스트가
/// 검증을 실패시키는 대신 항목을 빼기만 한다.
/// </summary>
public static class ArtifactHasher
{
    public static string? ComputeSha256Hex(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return Sha256Hex.HashStream(stream);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}