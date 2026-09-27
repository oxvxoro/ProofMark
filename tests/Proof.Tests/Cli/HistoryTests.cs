using System.Text.Json;
using Proof.Cli;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

public sealed class HistoryTests
{
    [Fact]
    public void Analyze_empty_history_is_all_zero()
    {
        var report = CertificateHistory.Analyze([], rule: null);

        Assert.Equal(0, report.TotalCertificates);
        Assert.Empty(report.VerdictDistribution);
        Assert.Empty(report.UnresolvedByRule);
        Assert.Null(report.FirstGeneratedAt);
        Assert.Null(report.LastGeneratedAt);
    }

    [Fact]
    public void Analyze_aggregates_verdicts_rules_and_reason_codes()
    {
        var entries = new List<HistoryEntry>
        {
            Entry(
                "20260901090000-a.summary.json",
                Summary(ProofVerdict.NotReady, "TEST_MAPPING_MISSING",
                    [Unresolved("P005", "TEST_MAPPING_MISSING"), Unresolved("P009", "MANUAL_REVIEW_MISSING")],
                    [new SummaryConstraint("TEST_MAPPING_MISSING", "msg", "sym")])),
            Entry(
                "20260902090000-b.summary.json",
                Summary(ProofVerdict.Uncertain, "IMPACT_POTENTIALLY_TRUNCATED",
                    [Unresolved("P005", "TEST_MAPPING_MISSING")],
                    [])),
        };

        var report = CertificateHistory.Analyze(entries, rule: null);

        Assert.Equal(2, report.TotalCertificates);
        Assert.Equal(1, report.VerdictDistribution["NOT_READY"]);
        Assert.Equal(1, report.VerdictDistribution["UNCERTAIN"]);
        Assert.Equal(2, report.UnresolvedByRule["P005"]);
        Assert.Equal(1, report.UnresolvedByRule["P009"]);
        Assert.Equal(3, report.TotalUnresolvedObligations);
        Assert.Equal(2, report.P005Unresolved);
        Assert.Equal(2, report.BlockingReasonCodes["TEST_MAPPING_MISSING"]);
        Assert.Equal(1, report.BlockingReasonCodes["IMPACT_POTENTIALLY_TRUNCATED"]);
        Assert.NotNull(report.FirstGeneratedAt);
        Assert.NotNull(report.LastGeneratedAt);
    }

    [Fact]
    public void Analyze_rule_filter_narrows_unresolved_counts_only()
    {
        var entries = new List<HistoryEntry>
        {
            Entry(
                "20260901090000-a.summary.json",
                Summary(ProofVerdict.NotReady, null,
                    [Unresolved("P005", "TEST_MAPPING_MISSING"), Unresolved("P009", "MANUAL_REVIEW_MISSING")],
                    [])),
        };

        var report = CertificateHistory.Analyze(entries, rule: "P005");

        Assert.Equal("P005", report.RuleFilter);
        Assert.Equal(1, report.UnresolvedByRule["P005"]);
        Assert.False(report.UnresolvedByRule.ContainsKey("P009"));
        Assert.Equal(1, report.TotalUnresolvedObligations);
        // 판정 분포는 규칙 포커스의 영향을 받지 않는다.
        Assert.Equal(1, report.VerdictDistribution["NOT_READY"]);
    }

    [Fact]
    public void Load_reads_sidecars_and_skips_invalid_json()
    {
        var directory = Path.Combine(Path.GetTempPath(), "proof-history-" + Guid.NewGuid().ToString("N"), ".proof", "certificates");
        Directory.CreateDirectory(directory);
        try
        {
            var summary = Summary(ProofVerdict.Proven, null, [], []);
            File.WriteAllText(
                Path.Combine(directory, "20260924090000-run.summary.json"),
                JsonSerializer.Serialize(summary, ProofJson.WireOptions));
            File.WriteAllText(Path.Combine(directory, "20260924090100-bad.summary.json"), "{ not json");

            var workspace = Directory.GetParent(directory)!.Parent!.FullName;
            var load = CertificateHistory.Load(workspace, limit: 0);

            Assert.Single(load.Entries);
            Assert.Equal(1, load.Skipped);
            Assert.Equal(new DateTimeOffset(2026, 9, 24, 9, 0, 0, TimeSpan.Zero), load.Entries[0].GeneratedAt);
        }
        finally
        {
            Directory.Delete(Directory.GetParent(directory)!.Parent!.FullName, recursive: true);
        }
    }

    [Fact]
    public void Render_includes_key_sections()
    {
        var report = CertificateHistory.Analyze(
            [Entry("20260901090000-a.summary.json", Summary(ProofVerdict.NotReady, "TEST_MAPPING_MISSING", [Unresolved("P005", "TEST_MAPPING_MISSING")], []))],
            rule: null);

        var markdown = CertificateHistory.Render(report);

        Assert.Contains("# Proof history", markdown, StringComparison.Ordinal);
        Assert.Contains("## Verdicts", markdown, StringComparison.Ordinal);
        Assert.Contains("NOT_READY: 1", markdown, StringComparison.Ordinal);
        Assert.Contains("P005: 1", markdown, StringComparison.Ordinal);
    }

    private static HistoryEntry Entry(string name, CertificateSummary summary)
    {
        var stamp = name[..name.IndexOf('-')];
        var generated = DateTimeOffset.ParseExact(
            stamp,
            "yyyyMMddHHmmss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
        return new HistoryEntry(name, generated, summary);
    }

    private static SummaryUnresolvedObligation Unresolved(string ruleId, string reasonCode)
        => new(ruleId, "claim", ObligationStatus.Unresolved, reasonCode, "subject", "file.cs");

    private static CertificateSummary Summary(
        ProofVerdict verdict,
        string? reasonCode,
        IReadOnlyList<SummaryUnresolvedObligation> unresolved,
        IReadOnlyList<SummaryConstraint> constraints)
        => new(verdict, reasonCode, "source", "certificate", "statement", 0, unresolved.Count, unresolved, constraints);
}
