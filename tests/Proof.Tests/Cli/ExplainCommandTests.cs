using System.Text.Json;
using Proof.Cli;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

public sealed class ExplainCommandTests
{
    [Fact]
    public void Explain_ReturnsLinksAndConstraints()
    {
        var obligation = new ProofObligation(
            "O1", "P005", ObligationKind.TestMapping, "mapping", "s1", true, 2, ["no mapped test relation found"],
            new ProofSubject(SubjectKind.Symbol, "s1", "App", DisplayName: "OrderService.Cancel"));
        var plan = new ProofPlan([obligation], SourceDigest: "src");
        var evidence = new VerificationEvidenceSet(
            [
                new ProofEvidence(
                    "TM1",
                    EvidenceKind.TestMapping,
                    "s2",
                    EvidenceStatus.Pass,
                    new EvidenceProvenance("test-map", SourceDigest: "src", CheckId: "test-mapping"))
            ],
            [
                new ObligationEvidenceLink("O1", "TM1", "rejected", 0, "BIND_SOURCE_DIGEST", ProofReasonCodes.EvidenceStaleSource, "stale")
            ]);
        var evaluation = new ProofEvaluation(ProofVerdict.Uncertain, [new EvaluatedObligation(obligation, ObligationStatus.Unresolved, ["TM1"])]);
        var certificate = new ChangeCertificateBuilder().Build(
            new ChangeImpact("base", "head", [], [], [], [], [], "complete", false, SourceDigest: "src"),
            plan,
            evidence,
            evaluation);

        var explanations = CertificateExplainer.Explain(certificate, "O1");

        var explanation = Assert.Single(explanations);
        Assert.Equal("P005", explanation.RuleId);
        Assert.Equal(ObligationStatus.Unresolved, explanation.Status);
        var link = Assert.Single(explanation.Links);
        Assert.Equal("rejected", link.Relation);
        Assert.Equal("BIND_SOURCE_DIGEST", link.BindingRuleId);
        Assert.Equal(EvidenceKind.TestMapping, link.Kind);
    }

    [Fact]
    public void Explain_P005Unresolved_IncludesNextStep()
    {
        var certificate = BuildCertificate("O1");

        var explanations = CertificateExplainer.Explain(certificate, "O1");

        var explanation = Assert.Single(explanations);
        Assert.NotNull(explanation.NextStep);
        Assert.Contains("proof map add --symbol", explanation.NextStep, StringComparison.Ordinal);
        Assert.Contains("--accept", explanation.NextStep, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("P001A", ObligationKind.Compatibility, ObligationStatus.Unresolved, "PackageValidation")]
    [InlineData("P008", ObligationKind.StaticAnalysis, ObligationStatus.Failed, "analyzers")]
    [InlineData("P009", ObligationKind.ManualReview, ObligationStatus.Unresolved, "proof review sign")]
    [InlineData("P011", ObligationKind.Architecture, ObligationStatus.Unresolved, "architecture.json")]
    [InlineData("P004", ObligationKind.Test, ObligationStatus.Unresolved, "proof verify")]
    public void Explain_OpenObligation_IncludesNextStep(
        string ruleId,
        ObligationKind kind,
        ObligationStatus status,
        string expectedFragment)
    {
        var subjectId = ruleId == "P009" ? "docs/asset.png" : "subject-1";
        var obligation = new ProofObligation(
            "O1", ruleId, kind, "claim", subjectId, true, 2, ["reason"]);
        var certificate = new ChangeCertificateBuilder().Build(
            new ChangeImpact("base", "head", [], [], [], [], [], "complete", false, SourceDigest: "src"),
            new ProofPlan([obligation], SourceDigest: "src"),
            new VerificationEvidenceSet([], []),
            new ProofEvaluation(ProofVerdict.Uncertain, [new EvaluatedObligation(obligation, status, [])]));

        var explanation = Assert.Single(CertificateExplainer.Explain(certificate, "O1"));
        Assert.NotNull(explanation.NextStep);
        Assert.Contains(expectedFragment, explanation.NextStep, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Explain_P005Proven_HasNoNextStep()
    {
        var obligation = new ProofObligation(
            "O1", "P005", ObligationKind.TestMapping, "mapping", "O1", true, 2, ["r"]);
        var certificate = new ChangeCertificateBuilder().Build(
            new ChangeImpact("base", "head", [], [], [], [], [], "complete", false, SourceDigest: "src"),
            new ProofPlan([obligation], SourceDigest: "src"),
            new VerificationEvidenceSet([], []),
            new ProofEvaluation(ProofVerdict.Uncertain, [new EvaluatedObligation(obligation, ObligationStatus.Proven, [])]));

        var explanation = Assert.Single(CertificateExplainer.Explain(certificate, "O1"));
        Assert.Null(explanation.NextStep);
    }

    [Fact]
    public void Explain_WithUnknownFilter_ReturnsNothing()
    {
        var certificate = BuildCertificate("O1");
        Assert.Empty(CertificateExplainer.Explain(certificate, "does-not-exist"));
    }

    [Fact]
    public void Diff_ReportsSetsOnlyInEach()
    {
        var left = BuildCertificate("O-left");
        var right = BuildCertificate("O-right");

        var diff = CertificateExplainer.Diff(left, right);

        Assert.Contains("O-left", diff.ObligationsOnlyInLeft);
        Assert.DoesNotContain("O-left", diff.ObligationsOnlyInRight);
        Assert.Contains("O-right", diff.ObligationsOnlyInRight);
    }

    [Fact]
    public void LatestCertificate_IgnoresSummarySidecar()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-explain-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, ".proof", "certificates");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "20260101000000-a.json"), "{}");
            File.WriteAllText(Path.Combine(directory, "20260101000000-a.summary.json"), "{}");
            File.WriteAllText(Path.Combine(directory, "20260102000000-b.json"), "{}");

            var latest = CertificateExplainer.LatestCertificate(root);

            Assert.EndsWith("20260102000000-b.json", latest, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadCertificate_InvalidJson_ReturnsError()
    {
        var path = Path.Combine(Path.GetTempPath(), "proof-explain-bad-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{ not json");
        try
        {
            Assert.Null(CertificateExplainer.LoadCertificate("root", path, out var error));
            Assert.NotNull(error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CertificateVerifyDiff_ReadsBothCertificates()
    {
        var left = Path.Combine(Path.GetTempPath(), "proof-diff-a-" + Guid.NewGuid().ToString("N") + ".json");
        var right = Path.Combine(Path.GetTempPath(), "proof-diff-b-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(left, JsonSerializer.Serialize(BuildCertificate("O-a"), ProofJson.WireOptions));
            File.WriteAllText(right, JsonSerializer.Serialize(BuildCertificate("O-b"), ProofJson.WireOptions));

            Assert.Equal(0, CertificateVerifyCommand.Diff(left, right));
            Assert.Equal(2, CertificateVerifyCommand.Diff(left, left + ".missing"));
        }
        finally
        {
            File.Delete(left);
            File.Delete(right);
        }
    }

    private static ChangeCertificate BuildCertificate(string obligationId)
    {
        var obligation = new ProofObligation(
            obligationId, "P005", ObligationKind.TestMapping, "mapping", obligationId, true, 2, ["r"]);
        var plan = new ProofPlan([obligation], SourceDigest: "src");
        var evaluation = new ProofEvaluation(
            ProofVerdict.Uncertain,
            [new EvaluatedObligation(obligation, ObligationStatus.Unresolved, [])]);
        return new ChangeCertificateBuilder().Build(
            new ChangeImpact("base", "head", [], [], [], [], [], "complete", false, SourceDigest: "src"),
            plan,
            new VerificationEvidenceSet([], []),
            evaluation);
    }
}