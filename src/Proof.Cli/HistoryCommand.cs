using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using Proof.Core;
using Proof.Engine;

namespace Proof.Cli;

/// <summary>
/// <c>.proof/certificates/*.summary.json</c> 사이드카를 읽어
/// 보증 추세를 보고한다. 이력은 분석 자료일 뿐이다. 과거의 PROVEN
/// 인증서는 현재 변경의 증거가 결코 아니며, 이 명령은
/// 판정 경로를 결코 건드리지 않는다.
/// </summary>
public static class HistoryCommand
{
    public static Command Create()
    {
        var rootOption = new Option<string?>("--root")
        {
            Description = "Workspace root. Defaults to the current directory.",
            DefaultValueFactory = _ => null
        };
        var ruleOption = new Option<string?>("--rule")
        {
            Description = "Focus unresolved obligations on one rule id (e.g. P005).",
            DefaultValueFactory = _ => null
        };
        var limitOption = new Option<int>("--limit")
        {
            Description = "Maximum number of certificates to read (0 = all).",
            DefaultValueFactory = _ => 0
        };
        var formatOption = new Option<string>("--format")
        {
            Description = "Output format: markdown or json.",
            DefaultValueFactory = _ => "markdown"
        };
        var command = new Command("history", "Summarize past certificate sidecars (analytics only; never proof evidence).")
        {
            rootOption,
            ruleOption,
            limitOption,
            formatOption
        };
        command.SetAction((parseResult, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Execute(
                parseResult.GetValue(rootOption),
                parseResult.GetValue(ruleOption),
                parseResult.GetValue(limitOption),
                parseResult.GetValue(formatOption)!));
        });
        return command;
    }

    internal static int Execute(string? root, string? rule, int limit, string format)
    {
        var workspace = string.IsNullOrWhiteSpace(root)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(root);
        var load = CertificateHistory.Load(workspace, limit);
        var report = CertificateHistory.Analyze(load.Entries, rule, load.Skipped);

        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(JsonSerializer.Serialize(report, ProofJson.WireOptions));
            return 0;
        }

        Console.WriteLine(CertificateHistory.Render(report));
        return 0;
    }
}

internal sealed record HistoryEntry(string Name, DateTimeOffset? GeneratedAt, CertificateSummary Summary);

internal sealed record HistoryCertificateView(
    string Name,
    DateTimeOffset? GeneratedAt,
    ProofVerdict Verdict,
    string? ReasonCode,
    int Unresolved);

internal sealed record HistoryReport(
    int TotalCertificates,
    int ParsedCertificates,
    int SkippedCertificates,
    string? RuleFilter,
    DateTimeOffset? FirstGeneratedAt,
    DateTimeOffset? LastGeneratedAt,
    IReadOnlyDictionary<string, int> VerdictDistribution,
    IReadOnlyDictionary<string, int> UnresolvedByRule,
    IReadOnlyDictionary<string, int> BlockingReasonCodes,
    int TotalUnresolvedObligations,
    int P005Unresolved,
    IReadOnlyList<HistoryCertificateView> Certificates);

internal static class CertificateHistory
{
    internal static (IReadOnlyList<HistoryEntry> Entries, int Skipped) Load(string workspaceRoot, int limit)
    {
        var directory = Path.Combine(workspaceRoot, ".proof", "certificates");
        if (!Directory.Exists(directory))
        {
            return ([], 0);
        }

        IEnumerable<FileInfo> files = Directory.EnumerateFiles(directory, "*.summary.json", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.Name, StringComparer.Ordinal)
            .ThenByDescending(file => file.LastWriteTimeUtc);
        if (limit > 0)
        {
            files = files.Take(limit);
        }

        var entries = new List<HistoryEntry>();
        var skipped = 0;
        foreach (var file in files)
        {
            try
            {
                var summary = JsonSerializer.Deserialize<CertificateSummary>(
                    File.ReadAllText(file.FullName), ProofJson.WireOptions);
                if (summary is null)
                {
                    skipped++;
                    continue;
                }

                entries.Add(new HistoryEntry(
                    file.Name,
                    ParseStamp(file.Name) ?? file.LastWriteTimeUtc,
                    summary));
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                skipped++;
            }
        }

        return (entries, skipped);
    }

    internal static HistoryReport Analyze(IReadOnlyList<HistoryEntry> entries, string? rule, int skipped = 0)
    {
        var verdicts = new Dictionary<string, int>(StringComparer.Ordinal);
        var unresolvedByRule = new Dictionary<string, int>(StringComparer.Ordinal);
        var reasonCodes = new Dictionary<string, int>(StringComparer.Ordinal);
        var certificates = new List<HistoryCertificateView>();
        var totalUnresolved = 0;
        var p005Unresolved = 0;

        foreach (var entry in entries)
        {
            var verdictKey = JsonNamingPolicy.SnakeCaseUpper.ConvertName(entry.Summary.Verdict.ToString());
            verdicts[verdictKey] = verdicts.GetValueOrDefault(verdictKey) + 1;

            if (!string.IsNullOrWhiteSpace(entry.Summary.ReasonCode))
            {
                Increment(reasonCodes, entry.Summary.ReasonCode!);
            }

            foreach (var constraint in entry.Summary.BlockingConstraints)
            {
                Increment(reasonCodes, constraint.Code);
            }

            var unresolved = 0;
            foreach (var obligation in entry.Summary.Unresolved)
            {
                if (!string.IsNullOrWhiteSpace(rule)
                    && !string.Equals(obligation.RuleId, rule, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                unresolved++;
                totalUnresolved++;
                Increment(unresolvedByRule, obligation.RuleId);
                if (string.Equals(obligation.RuleId, "P005", StringComparison.OrdinalIgnoreCase))
                {
                    p005Unresolved++;
                }
            }

            certificates.Add(new HistoryCertificateView(
                entry.Name,
                entry.GeneratedAt,
                entry.Summary.Verdict,
                entry.Summary.ReasonCode,
                unresolved));
        }

        return new HistoryReport(
            entries.Count + skipped,
            entries.Count,
            skipped,
            string.IsNullOrWhiteSpace(rule) ? null : rule,
            entries.Where(entry => entry.GeneratedAt is not null).Min(entry => entry.GeneratedAt),
            entries.Where(entry => entry.GeneratedAt is not null).Max(entry => entry.GeneratedAt),
            Order(verdicts),
            Order(unresolvedByRule),
            Order(reasonCodes),
            totalUnresolved,
            p005Unresolved,
            certificates);
    }

    internal static string Render(HistoryReport report)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("# Proof history");
        builder.AppendLine();
        builder.AppendLine($"Certificates: {report.TotalCertificates} (skipped {report.SkippedCertificates})");
        builder.AppendLine($"Range: {(report.FirstGeneratedAt is null ? "unknown" : report.FirstGeneratedAt.Value.ToUniversalTime().ToString("u"))} .. {(report.LastGeneratedAt is null ? "unknown" : report.LastGeneratedAt.Value.ToUniversalTime().ToString("u"))}");
        if (report.RuleFilter is not null)
        {
            builder.AppendLine($"Rule filter: {report.RuleFilter}");
        }

        builder.AppendLine();
        builder.AppendLine("## Verdicts");
        AppendCounts(builder, report.VerdictDistribution, "none");

        builder.AppendLine();
        builder.AppendLine("## Unresolved by rule");
        AppendCounts(builder, report.UnresolvedByRule, "none");
        builder.AppendLine($"Total unresolved: {report.TotalUnresolvedObligations} (P005: {report.P005Unresolved})");

        builder.AppendLine();
        builder.AppendLine("## Blocking reason codes");
        AppendCounts(builder, report.BlockingReasonCodes, "none");

        builder.AppendLine();
        builder.AppendLine("## Certificates");
        foreach (var certificate in report.Certificates)
        {
            builder.AppendLine($"- {certificate.Name}: {certificate.Verdict} (reason {certificate.ReasonCode ?? "none"}, unresolved {certificate.Unresolved})");
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendCounts(System.Text.StringBuilder builder, IReadOnlyDictionary<string, int> counts, string emptyLabel)
    {
        if (counts.Count == 0)
        {
            builder.AppendLine($"- {emptyLabel}");
            return;
        }

        foreach (var pair in counts)
        {
            builder.AppendLine($"- {pair.Key}: {pair.Value}");
        }
    }

    private static IReadOnlyDictionary<string, int> Order(Dictionary<string, int> counts)
        => counts.OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static void Increment(Dictionary<string, int> counts, string key)
        => counts[key] = counts.GetValueOrDefault(key) + 1;

    private static DateTimeOffset? ParseStamp(string fileName)
    {
        var stamp = Path.GetFileName(fileName);
        var dash = stamp.IndexOf('-');
        if (dash > 0)
        {
            stamp = stamp[..dash];
        }

        return DateTimeOffset.TryParseExact(
            stamp,
            "yyyyMMddHHmmss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }
}
