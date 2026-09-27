using Proof.Cli;

namespace Proof.Tests;

public sealed class CertificateFileNameTests
{
    [Fact]
    public void IsCertificateFile_ExcludesSummarySidecar()
    {
        Assert.True(VerifyCommand.IsCertificateFile("20260101120000-proof-abc.json"));
        Assert.False(VerifyCommand.IsCertificateFile("20260101120000-proof-abc.summary.json"));
        var names = new[]
        {
            "20260101120000-proof-abc.json",
            "20260101120000-proof-abc.summary.json"
        };
        var latestIfUnfiltered = names.OrderBy(value => value, StringComparer.Ordinal).Last();
        Assert.Equal("20260101120000-proof-abc.summary.json", latestIfUnfiltered);
        var latestCertificate = names.Where(VerifyCommand.IsCertificateFile).OrderBy(value => value, StringComparer.Ordinal).Last();
        Assert.Equal("20260101120000-proof-abc.json", latestCertificate);
    }
}
