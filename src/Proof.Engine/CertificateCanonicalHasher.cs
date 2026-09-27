using System.Reflection;
using System.Text.Json;
using Proof.Core;

namespace Proof.Engine;

public static class CertificateCanonicalHasher
{
    public static string ComputeDigest(ChangeCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        if (certificate.SchemaVersion >= 3)
        {
            return ComputeStatementDigest(certificate);
        }

        var payload = certificate with { CertificateDigest = null };
        var json = JsonSerializer.Serialize(payload, ProofJson.WireOptions);
        return Sha256Hex.HashText(json);
    }

    public static string ComputeStatementDigest(ChangeCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        var payload = new
        {
            certificate.SchemaVersion,
            certificate.BaseRevision,
            certificate.HeadRevision,
            certificate.Verdict,
            certificate.Impact,
            certificate.Plan,
            certificate.Evidence,
            certificate.Evaluation,
            certificate.SourceDigest,
            certificate.Toolchain,
            certificate.VerificationPlan,
            certificate.Constraints,
            certificate.ArtifactManifest
        };
        var json = JsonSerializer.Serialize(payload, ProofJson.WireOptions);
        return Sha256Hex.HashText(json);
    }

    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Sha256Hex.HashStream(stream);
    }

    public static string HashText(string text)
        => Sha256Hex.HashText(text);

    // 단일 파일 게시처럼 경로가 없거나 읽지 못하면 null이다. 판정에는 쓰지 않는다.
    public static string? AssemblyFileSha256(System.Reflection.Assembly? assembly)
    {
        var location = assembly?.Location;
        if (string.IsNullOrWhiteSpace(location) || !File.Exists(location))
        {
            return null;
        }

        try
        {
            return HashFile(location);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static string AssemblyVersion(Type type)
        => type.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
           ?? type.Assembly.GetName().Version?.ToString()
           ?? "unknown";
}
