using Proof.Core;

namespace Proof.Core;

public static class Sha256Hex
{
    public static string HashText(string text)
        => HashBytes(System.Text.Encoding.UTF8.GetBytes(text));

    public static string HashBytes(ReadOnlySpan<byte> bytes)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string HashStream(Stream stream)
        => HashBytes(System.Security.Cryptography.SHA256.HashData(stream));
}
