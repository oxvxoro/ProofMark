using Proof.Cli;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

public sealed class SummaryMarkdownTests
{
    private static CertificateSummary Summary(
        int unresolved = 1,
        int blocking = 1)
        => new(
            ProofVerdict.Uncertain,
            ReasonCode: "REQUIRED_EVIDENCE_MISSING",
            SourceDigest: "src",
            CertificateDigest: "cert",
            StatementDigest: "stmt",
            ProvenObligations: 4,
            UnresolvedObligations: unresolved,
            Unresolved: [.. Enumerable.Range(0, unresolved).Select(index => new SummaryUnresolvedObligation(
                $"P005",
                $"Claim {index}",
                ObligationStatus.Unresolved,
                "REQUIRED_EVIDENCE_MISSING",
                $"symbol-{index}",
                $"File{index}.cs"))],
            BlockingConstraints: [.. Enumerable.Range(0, blocking).Select(index => new SummaryConstraint(
                $"CODE_{index}",
                $"constraint {index}",
                null))]);

    [Fact]
    public void Render_IncludesVerdictCountsAndUnresolved()
    {
        var markdown = SummaryMarkdown.Render(Summary());

        Assert.Contains("UNCERTAIN", markdown, StringComparison.Ordinal);
        Assert.Contains("REQUIRED_EVIDENCE_MISSING", markdown, StringComparison.Ordinal);
        Assert.Contains("4 proven / 1 unresolved", markdown, StringComparison.Ordinal);
        Assert.Contains("symbol-0", markdown, StringComparison.Ordinal);
        Assert.Contains("CODE_0", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_CapsUnresolvedList()
    {
        var markdown = SummaryMarkdown.Render(Summary(unresolved: 45, blocking: 0));

        Assert.Contains("### Unresolved (45)", markdown, StringComparison.Ordinal);
        Assert.Contains("symbol-29", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("symbol-30", markdown, StringComparison.Ordinal);
        Assert.Contains("... and 15 more", markdown, StringComparison.Ordinal);
    }
}
