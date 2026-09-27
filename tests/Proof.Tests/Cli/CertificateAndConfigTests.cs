using System.Text.Json;
using Proof.Adapters.Distill;
using Proof.Cli;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

[Collection("AttestationEnvironment")]
public sealed class CertificateAndConfigTests
{
    [Fact]
    public void CertificateMetadata_IsDirtyDefaultsToFalse()
    {
        var metadata = new CertificateMetadata();
        Assert.False(metadata.IsDirty);

        var certificate = new ChangeCertificateBuilder().Build(
            new ChangeImpact("abc", "def", [], [], [], [], [], "complete", false, SourceDigest: "src"),
            new ProofPlan([], ChangeSetIsEmpty: true, SourceDigest: "src"),
            new VerificationEvidenceSet([], []),
            new ProofEvaluation(ProofVerdict.NoChange, []),
            new CertificateMetadata(RunId: "proof-fixed"));
        Assert.False(certificate.IsDirty);

        var withoutMetadata = new ChangeCertificateBuilder().Build(
            new ChangeImpact("abc", "def", [], [], [], [], [], "complete", false, SourceDigest: "src"),
            new ProofPlan([], ChangeSetIsEmpty: true, SourceDigest: "src"),
            new VerificationEvidenceSet([], []),
            new ProofEvaluation(ProofVerdict.NoChange, []));
        Assert.False(withoutMetadata.IsDirty);
    }
    [Fact]
    public void CertificateDigest_IsStableForSamePayload()
    {
        var impact = new ChangeImpact("abc", "def", [], [], [], [], [], "complete", false, SourceDigest: "src");
        var plan = new ProofPlan([], ChangeSetIsEmpty: true, SourceDigest: "src");
        var evidence = new VerificationEvidenceSet([], []);
        var evaluation = new ProofEvaluation(ProofVerdict.NoChange, []);
        var first = new ChangeCertificateBuilder().Build(impact, plan, evidence, evaluation, new CertificateMetadata(RunId: "proof-fixed"));
        var second = new ChangeCertificateBuilder().Build(impact, plan, evidence, evaluation, new CertificateMetadata(RunId: "proof-fixed"));
        Assert.Equal(first.CertificateDigest, second.CertificateDigest);
        Assert.Equal(3, first.SchemaVersion);
        Assert.Equal(first.CertificateDigest, CertificateCanonicalHasher.ComputeDigest(first));
        var otherRun = new ChangeCertificateBuilder().Build(impact, plan, evidence, evaluation, new CertificateMetadata(RunId: "proof-other"));
        Assert.Equal(first.CertificateDigest, otherRun.CertificateDigest);
        Assert.Equal(first.StatementDigest, otherRun.StatementDigest);
    }

    [Fact]
    public void StatementDigest_IgnoresAbsoluteArtifactPointer()
    {
        var impact = new ChangeImpact("abc", "def", [], [], [], [], [], "complete", false, SourceDigest: "src");
        var plan = new ProofPlan([], ChangeSetIsEmpty: true, SourceDigest: "src");
        var left = EvidenceWithPointer("C:/work-a/.distill/runs/a/out.json");
        var right = EvidenceWithPointer("D:/work-b/.distill/runs/a/out.json");
        var evaluation = new ProofEvaluation(ProofVerdict.NoChange, []);
        var first = new ChangeCertificateBuilder().Build(impact, plan, left, evaluation, new CertificateMetadata(RunId: "proof-fixed"));
        var second = new ChangeCertificateBuilder().Build(impact, plan, right, evaluation, new CertificateMetadata(RunId: "proof-fixed"));
        Assert.Equal(first.StatementDigest, second.StatementDigest);
        Assert.DoesNotContain('\\', first.Evidence.Evidence[0].Provenance.ArtifactPointer ?? string.Empty);
        Assert.Equal(".distill/out.json", first.Evidence.Evidence[0].Provenance.ArtifactPointer);
    }

    [Fact]
    public void StatementDigest_IgnoresDistillRunIdSegment()
    {
        var impact = new ChangeImpact("abc", "def", [], [], [], [], [], "complete", false, SourceDigest: "src");
        var plan = new ProofPlan([], ChangeSetIsEmpty: true, SourceDigest: "src");
        var left = EvidenceWithPointer(@"C:\repo\.distill\runs\d-20260101-000000-aaaaaa\build\stdout.log");
        var right = EvidenceWithPointer("/home/ci/repo/.distill/runs/d-20260102-111111-bbbbbb/build/stdout.log");
        var evaluation = new ProofEvaluation(ProofVerdict.NoChange, []);
        var first = new ChangeCertificateBuilder().Build(impact, plan, left, evaluation, new CertificateMetadata(RunId: "proof-a"));
        var second = new ChangeCertificateBuilder().Build(impact, plan, right, evaluation, new CertificateMetadata(RunId: "proof-b"));
        Assert.Equal(".distill/build/stdout.log", first.Evidence.Evidence[0].Provenance.ArtifactPointer);
        Assert.Equal(first.StatementDigest, second.StatementDigest);
        Assert.DoesNotContain("d-20260101", first.Evidence.Evidence[0].Provenance.ArtifactPointer);
    }

    [Fact]
    public void StatementDigest_DoesNotRehashFromDisk()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-rehash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var artifact = Path.Combine(root, "artifact.txt");
        try
        {
            File.WriteAllText(artifact, "one");
            var impact = new ChangeImpact("abc", "def", [], [], [], [], [], "complete", false, SourceDigest: "src");
            var plan = new ProofPlan([], ChangeSetIsEmpty: true, SourceDigest: "src");
            var evidence = new VerificationEvidenceSet(
                [
                    new ProofEvidence(
                        "E1",
                        EvidenceKind.Build,
                        "build",
                        EvidenceStatus.Pass,
                        new EvidenceProvenance("distill", artifact, "src", "build", Sha256: "fixed-sha"))
                ],
                []);
            var evaluation = new ProofEvaluation(ProofVerdict.NoChange, []);
            var certificate = new ChangeCertificateBuilder().Build(
                impact,
                plan,
                evidence,
                evaluation,
                new CertificateMetadata(RunId: "proof-fixed", WorkspaceRoot: root));
            Assert.Equal("fixed-sha", certificate.Evidence.Evidence[0].Provenance.Sha256);
            File.WriteAllText(artifact, "two");
            var again = new ChangeCertificateBuilder().Build(
                impact,
                plan,
                evidence,
                evaluation,
                new CertificateMetadata(RunId: "proof-fixed", WorkspaceRoot: root));
            Assert.Equal(certificate.StatementDigest, again.StatementDigest);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SchemaV2_UsesLegacyDigestAndVerifySucceeds()
    {
        var impact = new ChangeImpact("abc", "def", [], [], [], [], [], "complete", false, SourceDigest: "src");
        var plan = new ProofPlan([], ChangeSetIsEmpty: true, SourceDigest: "src");
        var evidence = new VerificationEvidenceSet([], []);
        var evaluation = new ProofEvaluation(ProofVerdict.NoChange, []);
        var v2 = new ChangeCertificate(
            SchemaVersion: 2,
            BaseRevision: "abc",
            HeadRevision: "def",
            Verdict: ProofVerdict.NoChange,
            Impact: impact,
            Plan: plan,
            Evidence: evidence,
            Evaluation: evaluation,
            RunId: "legacy-run",
            SourceDigest: "src",
            CertificateDigest: null);
        var digest = CertificateCanonicalHasher.ComputeDigest(v2);
        Assert.NotEqual(CertificateCanonicalHasher.ComputeStatementDigest(v2 with { SchemaVersion = 3 }), digest);
        var sealedV2 = v2 with { CertificateDigest = digest };
        var path = Path.Combine(Path.GetTempPath(), "proof-v2-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(sealedV2, ProofJson.WireOptions));
            Assert.Equal(0, CertificateVerifyCommand.Verify(path));
            var tampered = Path.Combine(Path.GetTempPath(), "proof-v2-tamper-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(tampered, JsonSerializer.Serialize(sealedV2 with { Verdict = ProofVerdict.Proven }, ProofJson.WireOptions));
            Assert.Equal(1, CertificateVerifyCommand.Verify(tampered));
            File.Delete(tampered);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void GoldenV3_RoundTripMatchesStatementDigest()
    {
        var impact = new ChangeImpact("abc", "def", [], [], [], [], [], "complete", false, SourceDigest: "src");
        var plan = new ProofPlan([], ChangeSetIsEmpty: true, SourceDigest: "src");
        var evidence = EvidenceWithPointer("C:/repo/.distill/runs/d-20260101-000000-aaaaaa/normalized.json");
        var evaluation = new ProofEvaluation(ProofVerdict.NoChange, []);
        var certificate = new ChangeCertificateBuilder().Build(
            impact,
            plan,
            evidence,
            evaluation,
            new CertificateMetadata(RunId: "proof-golden"));
        var json = JsonSerializer.Serialize(certificate, ProofJson.WireOptions);
        var loaded = JsonSerializer.Deserialize<ChangeCertificate>(json, ProofJson.WireOptions)!;
        Assert.Equal(certificate.StatementDigest, CertificateCanonicalHasher.ComputeStatementDigest(loaded));
        Assert.Equal(certificate.CertificateDigest, CertificateCanonicalHasher.ComputeDigest(loaded));
        Assert.Equal(".distill/normalized.json", loaded.Evidence.Evidence[0].Provenance.ArtifactPointer);
        var path = Path.Combine(Path.GetTempPath(), "proof-v3-golden-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, json);
            Assert.Equal(0, CertificateVerifyCommand.Verify(path));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void TamperedCertificatePayload_FailsDigestCheck()
    {
        var impact = new ChangeImpact("abc", "def", [], [], [], [], [], "complete", false, SourceDigest: "src");
        var plan = new ProofPlan([], ChangeSetIsEmpty: true, SourceDigest: "src");
        var evidence = new VerificationEvidenceSet([], []);
        var evaluation = new ProofEvaluation(ProofVerdict.NoChange, []);
        var certificate = new ChangeCertificateBuilder().Build(impact, plan, evidence, evaluation, new CertificateMetadata(RunId: "proof-fixed"));
        var tampered = certificate with { Verdict = ProofVerdict.Proven };
        Assert.NotEqual(certificate.CertificateDigest, CertificateCanonicalHasher.ComputeDigest(tampered));
        Assert.NotEqual(certificate.StatementDigest, CertificateCanonicalHasher.ComputeStatementDigest(tampered));
    }

    [Fact]
    public void CertificateVerify_UnsignedAttestation_IsAcceptedByDefault()
    {
        var impact = new ChangeImpact("abc", "def", [], [], [], [], [], "complete", false, SourceDigest: "src");
        var plan = new ProofPlan([], SourceDigest: "src");
        var certificate = new ChangeCertificateBuilder().Build(
            impact, plan, new VerificationEvidenceSet([], []), new ProofEvaluation(ProofVerdict.NoChange, []));
        var path = Path.Combine(Path.GetTempPath(), "proof-attest-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(certificate, ProofJson.WireOptions));
            Assert.Equal(0, CertificateVerifyCommand.Verify(path, requireSigned: false));
            Assert.Equal(1, CertificateVerifyCommand.Verify(path, requireSigned: true));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void CertificateVerify_MismatchedAttestationStatementDigest_Fails()
    {
        Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, "cert-verify-mismatch-key");
        try
        {
            var impact = new ChangeImpact("abc", "def", [], [], [], [], [], "complete", false, SourceDigest: "src");
            var plan = new ProofPlan([], SourceDigest: "src");
            var certificate = new ChangeCertificateBuilder().Build(
                impact, plan, new VerificationEvidenceSet([], []), new ProofEvaluation(ProofVerdict.NoChange, []));
            var detachedDigest = "deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef";
            var payload = AttestationHmac.ComputeHex("cert-verify-mismatch-key", detachedDigest);
            certificate = certificate with
            {
                Attestation = new AttestationEnvelope(
                    detachedDigest,
                    "env:PROOF_ATTESTATION_HMAC_KEY",
                    "hmac-sha256",
                    payload)
            };
            var path = Path.Combine(Path.GetTempPath(), "proof-attest-mismatch-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(path, JsonSerializer.Serialize(certificate, ProofJson.WireOptions));
                Assert.Equal(1, CertificateVerifyCommand.Verify(path, requireSigned: false));
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, null);
        }
    }

    [Fact]
    public void CertificateVerify_UnsignedAttestationEnvelope_IsTrustedByDefaultVerifier()
    {
        var impact = new ChangeImpact("abc", "def", [], [], [], [], [], "complete", false, SourceDigest: "src");
        var plan = new ProofPlan([], SourceDigest: "src");
        var certificate = new ChangeCertificateBuilder().Build(
            impact, plan, new VerificationEvidenceSet([], []), new ProofEvaluation(ProofVerdict.NoChange, []));
        certificate = certificate with
        {
            Attestation = new AttestationEnvelope(
                certificate.StatementDigest ?? string.Empty,
                "unsigned",
                "none")
        };
        var path = Path.Combine(Path.GetTempPath(), "proof-attest2-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(certificate, ProofJson.WireOptions));
            Assert.Equal(0, CertificateVerifyCommand.Verify(path));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static VerificationEvidenceSet EvidenceWithPointer(string pointer)
        => new(
            [
                new ProofEvidence(
                    "E1",
                    EvidenceKind.Build,
                    "build",
                    EvidenceStatus.Pass,
                    new EvidenceProvenance("distill", pointer, "src", "build"))
            ],
            []);

    [Fact]
    public void CertificateVerify_TrustedIssuerAllowlist_ControlsAcceptance()
    {
        var impact = new ChangeImpact("abc", "def", [], [], [], [], [], "complete", false, SourceDigest: "src");
        var plan = new ProofPlan([], SourceDigest: "src");
        var certificate = new ChangeCertificateBuilder().Build(
            impact, plan, new VerificationEvidenceSet([], []), new ProofEvaluation(ProofVerdict.NoChange, []));
        certificate = certificate with
        {
            Attestation = new AttestationEnvelope(certificate.StatementDigest ?? string.Empty, "unsigned", "none")
        };
        var path = Path.Combine(Path.GetTempPath(), "proof-issuer-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(certificate, ProofJson.WireOptions));
            Assert.Equal(0, CertificateVerifyCommand.Verify(path, trustedIssuers: ["unsigned"]));
            Assert.Equal(1, CertificateVerifyCommand.Verify(path, trustedIssuers: ["other"]));

            // 증명이 전혀 없으면 발급자 허용 목록을 만족할 수 없다.
            var withoutAttestation = certificate with { Attestation = null };
            File.WriteAllText(path, JsonSerializer.Serialize(withoutAttestation, ProofJson.WireOptions));
            Assert.Equal(1, CertificateVerifyCommand.Verify(path, trustedIssuers: ["unsigned"]));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void RunId_IsDeterministicForSameSourceDigest()
    {
        var impact = new ChangeImpact("abc", "def", [], [], [], [], [], "complete", false, SourceDigest: "src");
        var plan = new ProofPlan([], SourceDigest: "src");
        var evidence = new VerificationEvidenceSet([], []);
        var evaluation = new ProofEvaluation(ProofVerdict.NoChange, []);
        var first = new ChangeCertificateBuilder().Build(impact, plan, evidence, evaluation);
        var second = new ChangeCertificateBuilder().Build(impact, plan, evidence, evaluation);
        Assert.Equal(first.RunId, second.RunId);
        Assert.StartsWith("proof-src-", first.RunId);
    }

    [Fact]
    public void RunId_WithoutSourceDigest_UsesNoSrcPrefix()
    {
        var impact = new ChangeImpact("abc", "def", [], [], [], [], [], "complete", false);
        var plan = new ProofPlan([]);
        var certificate = new ChangeCertificateBuilder().Build(
            impact, plan, new VerificationEvidenceSet([], []), new ProofEvaluation(ProofVerdict.NoChange, []));
        Assert.StartsWith("proof-nosrc-", certificate.RunId);
    }

    [Fact]
    public void IsDirty_FollowsProvidedMetadata()
    {
        var impact = new ChangeImpact("abc", "def", [], [], [], [], [], "complete", false, SourceDigest: "src");
        var plan = new ProofPlan([], SourceDigest: "src");
        var evidence = new VerificationEvidenceSet([], []);
        var evaluation = new ProofEvaluation(ProofVerdict.NoChange, []);
        var clean = new ChangeCertificateBuilder().Build(impact, plan, evidence, evaluation, new CertificateMetadata(IsDirty: false));
        var dirty = new ChangeCertificateBuilder().Build(impact, plan, evidence, evaluation, new CertificateMetadata(IsDirty: true));
        Assert.False(clean.IsDirty);
        Assert.True(dirty.IsDirty);
    }

    [Fact]
    public async Task ArtifactPointerOnDisk_ProducesManifestSha256Entry()
    {
        var path = Path.Combine(Path.GetTempPath(), "proof-artifact-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(path, "payload");
            var sha256 = DistillVerificationRunner.ComputeArtifactSha256(path);
            Assert.NotNull(sha256);
            Assert.Equal(64, sha256!.Length);
            Assert.All(sha256, character => Assert.True(Uri.IsHexDigit(character)));
            Assert.Null(DistillVerificationRunner.ComputeArtifactSha256(path + ".missing"));

            var impact = new ChangeImpact("abc", "def", [], [], [], [], [], "complete", false, SourceDigest: "src");
            var plan = new ProofPlan([], SourceDigest: "src");
            var evidence = new VerificationEvidenceSet(
                [new ProofEvidence(
                    "E1",
                    EvidenceKind.Build,
                    "build",
                    EvidenceStatus.Pass,
                    new EvidenceProvenance("distill", path, "src", "build", sha256))],
                []);
            var certificate = new ChangeCertificateBuilder().Build(
                impact,
                plan,
                evidence,
                new ProofEvaluation(ProofVerdict.NoChange, []),
                new CertificateMetadata(WorkspaceRoot: Path.GetDirectoryName(path)));
            var entry = Assert.Single(certificate.ArtifactManifest ?? []);
            Assert.Equal(64, entry.Sha256.Length);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void ConfigValidate_RejectsEmptyTestMapTestName()
    {
        var config = new ProofConfig();
        config.Policy.TestMaps.Add(new TestMapEntrySection { Symbol = "OrderService.Cancel", Tests = ["  "] });

        var exception = Assert.Throws<ProofConfigException>(() => ConfigValidateCommand.ValidateTestMaps(config));
        Assert.Contains("OrderService.Cancel", exception.Message);
    }

    [Fact]
    public void ConfigValidate_RejectsEmptyTestMapSymbol()
    {
        var config = new ProofConfig();
        config.Policy.TestMaps.Add(new TestMapEntrySection { Symbol = " ", Tests = ["Proof.Tests.X.Y"] });

        Assert.Throws<ProofConfigException>(() => ConfigValidateCommand.ValidateTestMaps(config));
    }

    [Fact]
    public void ConfigValidate_AcceptsWellFormedTestMap()
    {
        var config = new ProofConfig();
        config.Policy.TestMaps.Add(new TestMapEntrySection
        {
            Symbol = "OrderService.Cancel",
            Tests = ["Proof.Tests.OrderServiceTests.Cancel_works"]
        });

        ConfigValidateCommand.ValidateTestMaps(config);
    }

    [Fact]
    public void ConfigValidate_RejectsBareMethodNameTest()
    {
        var config = new ProofConfig();
        config.Policy.TestMaps.Add(new TestMapEntrySection
        {
            Symbol = "OrderService.Cancel",
            Tests = ["Cancel_works"]
        });

        var exception = Assert.Throws<ProofConfigException>(() => ConfigValidateCommand.ValidateTestMaps(config));
        Assert.Contains("bare test name", exception.Message);
    }

    [Fact]
    public void UnknownProofPolicyProperty_IsRejected()
    {
        var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
            .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance)
            .Build();
        var yaml = """
            version: 2
            proof:
              base:
                strategy: mergeBase
            verification:
              distillProfile: quick
            policy:
              unexpectedField: true
            """;
        var exception = Assert.Throws<ProofConfigException>(() => ProofConfig.RejectUnknownProperties(yaml, deserializer));
        Assert.Contains("unexpectedField", exception.Message);
    }
}