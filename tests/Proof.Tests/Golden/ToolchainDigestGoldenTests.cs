using System.Text.Json;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests.Golden;

public sealed class ToolchainDigestGoldenTests
{
    private static readonly ToolchainIdentity Baseline = new("1.0.0", "2.0.0", "3.0.0", "proof-config", "distill-config");

    private static ChangeCertificate Certificate(ToolchainIdentity toolchain)
        => GoldenScenarioFactory.BuildCertificate() with { Toolchain = toolchain };

    [Fact]
    public void NullBinaryDigests_KeepStatementDigest()
    {
        var digest = CertificateCanonicalHasher.ComputeStatementDigest(Certificate(Baseline));
        var again = CertificateCanonicalHasher.ComputeStatementDigest(
            Certificate(Baseline with
            {
                ProofBinarySha256 = null,
                CodeMapBinarySha256 = null,
                DistillBinarySha256 = null
            }));
        Assert.Equal(digest, again);
    }

    [Fact]
    public void NullBinaryDigests_AreOmittedFromWireJson()
    {
        var json = JsonSerializer.Serialize(Baseline, ProofJson.WireOptions);

        Assert.DoesNotContain("BinarySha256", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BinaryDigest_WhenPresent_EntersStatement()
    {
        var baseline = CertificateCanonicalHasher.ComputeStatementDigest(Certificate(Baseline));
        var withDigest = Baseline with { ProofBinarySha256 = new string('a', 64) };

        Assert.NotEqual(
            baseline,
            CertificateCanonicalHasher.ComputeStatementDigest(Certificate(withDigest)));
        Assert.Contains("proofBinarySha256", JsonSerializer.Serialize(withDigest, ProofJson.WireOptions), StringComparison.Ordinal);
    }

    [Fact]
    public void CertificateVerify_ToolchainBinary_ComparesOnlyWhenRequested()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-toolchain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var binary = Path.Combine(root, "proof.dll");
            File.WriteAllText(binary, "binary-v1");
            var other = Path.Combine(root, "other.dll");
            File.WriteAllText(other, "binary-v2");

            var recorded = Write(root, "recorded.json", new CertificateMetadata(ProofBinarySha256: CertificateCanonicalHasher.HashFile(binary)));
            Assert.Equal(0, Proof.Cli.CertificateVerifyCommand.Verify(recorded));
            Assert.Equal(0, Proof.Cli.CertificateVerifyCommand.Verify(recorded, toolchainBinaries: [binary]));
            Assert.Equal(1, Proof.Cli.CertificateVerifyCommand.Verify(recorded, toolchainBinaries: [other]));
            Assert.Equal(2, Proof.Cli.CertificateVerifyCommand.Verify(recorded, toolchainBinaries: [Path.Combine(root, "missing.dll")]));

            var empty = Write(root, "empty.json", new CertificateMetadata());
            Assert.Equal(0, Proof.Cli.CertificateVerifyCommand.Verify(empty, toolchainBinaries: [other]));
            Assert.Equal(2, Proof.Cli.CertificateVerifyCommand.Verify(empty, toolchainBinaries: [Path.Combine(root, "missing.dll")]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Write(string root, string name, CertificateMetadata metadata)
    {
        var impact = new ChangeImpact("abc", "def", [], [], [], [], [], "complete", false, SourceDigest: "src");
        var certificate = new ChangeCertificateBuilder().Build(
            impact,
            new ProofPlan([], SourceDigest: "src"),
            new VerificationEvidenceSet([], []),
            new ProofEvaluation(ProofVerdict.NoChange, []),
            metadata);
        var path = Path.Combine(root, name);
        File.WriteAllText(path, JsonSerializer.Serialize(certificate, ProofJson.WireOptions));
        return path;
    }
}
