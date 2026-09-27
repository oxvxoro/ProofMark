using Proof.Core;
using Proof.Engine;

namespace Proof.Cli;

/// <summary>
/// 인증서 요약의 결정적 마크다운 렌더링. 렌더러 하나를
/// step summary, PR comment, `proof summary --format
/// markdown`이 공유하므로 CI와 CLI가 어긋날 수 없다.
/// </summary>
internal static class SummaryMarkdown
{
    public const int MaxListedUnresolved = 30;

    public static string Render(CertificateSummary summary)
    {
        var lines = new List<string>
        {
            "## Proof verdict",
            string.Empty,
            $"**{summary.Verdict.ToString().ToUpperInvariant()}**"
        };
        if (!string.IsNullOrWhiteSpace(summary.ReasonCode))
        {
            lines.Add($"Reason: `{summary.ReasonCode}`");
        }

        lines.Add(string.Empty);
        lines.Add($"Obligations: {summary.ProvenObligations} proven / {summary.UnresolvedObligations} unresolved");
        if (summary.Unresolved.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"### Unresolved ({summary.Unresolved.Count})");
            foreach (var item in summary.Unresolved.Take(MaxListedUnresolved))
            {
                var subject = item.SubjectId ?? item.Claim;
                var location = item.File ?? "unknown file";
                lines.Add($"- `{item.RuleId}` {subject} ({location})");
            }

            if (summary.Unresolved.Count > MaxListedUnresolved)
            {
                lines.Add($"- ... and {summary.Unresolved.Count - MaxListedUnresolved} more");
            }
        }

        if (summary.BlockingConstraints.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"### Blocking constraints ({summary.BlockingConstraints.Count})");
            foreach (var constraint in summary.BlockingConstraints)
            {
                lines.Add($"- `{constraint.Code}` {constraint.Message}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }
}
