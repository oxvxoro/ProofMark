using System.Text.Json;
using Proof.Cli;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

public sealed class CertificateSummaryTests
{
    [Fact]
    public void ToSummary_ContainsCountsAndDoesNotAffectStatementDigest()
    {
        var obligation = new ProofObligation("O1", "P004", ObligationKind.Test, "claim", "t", true, 4, [ProofReasonCodes.RequiredEvidenceMissing]);
        var proven = new ProofObligation("O2", "P006", ObligationKind.Uncertainty, "claim", "u", true, 1, ["r"]);
        var impact = new ChangeImpact("base", "head", [], [], [], [], [], "complete", false);
        var plan = new ProofPlan([obligation, proven]);
        var bound = new VerificationEvidenceSet([], []);
        var evaluation = new ProofEvaluation(
            ProofVerdict.Uncertain,
            [new EvaluatedObligation(obligation, ObligationStatus.Unresolved, []), new EvaluatedObligation(proven, ObligationStatus.Proven, [])],
            ReasonCode: ProofReasonCodes.RequiredEvidenceMissing);
        var certificate = new ChangeCertificateBuilder().Build(impact, plan, bound, evaluation);
        var before = certificate.StatementDigest;

        var summary = ChangeCertificateBuilder.ToSummary(certificate with { StatementDigest = "changed-by-sidecar" });

        Assert.Equal(1, summary.ProvenObligations);
        Assert.Equal(1, summary.UnresolvedObligations);
        Assert.Equal("P004", Assert.Single(summary.Unresolved).RuleId);
        Assert.Equal(ProofReasonCodes.RequiredEvidenceMissing, Assert.Single(summary.Unresolved).ReasonCode);
        Assert.Equal("t", Assert.Single(summary.Unresolved).SubjectId);
        Assert.Equal(before, certificate.StatementDigest);
    }

    [Fact]
    public void SummaryCommand_LoadsSummarySidecarWithoutRebuilding()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-summary-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, ".proof", "certificates");
        Directory.CreateDirectory(directory);
        var summaryPath = Path.Combine(directory, "20260101000000-proof-abc.summary.json");
        var summary = new CertificateSummary(
            ProofVerdict.Uncertain,
            ProofReasonCodes.ImpactLocationUnknown,
            "source",
            "certificate",
            "statement",
            1,
            1,
            [new SummaryUnresolvedObligation("P006", "claim", ObligationStatus.Unresolved, ProofReasonCodes.ImpactLocationUnknown, "subject", "file.cs")],
            []);
        File.WriteAllText(summaryPath, JsonSerializer.Serialize(summary, ProofJson.WireOptions));
        try
        {
            var previous = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(root);
            try
            {
                var code = SummaryCommand.Execute(summaryPath, "json");
                Assert.Equal(0, code);
            }
            finally
            {
                Directory.SetCurrentDirectory(previous);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
