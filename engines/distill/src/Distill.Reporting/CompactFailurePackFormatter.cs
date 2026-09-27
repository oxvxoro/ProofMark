using System.Text;
using Distill.Core.Diagnostics;
using Distill.Core.Planning;
using Distill.Core.Redaction;
using Distill.Core.Runs;
using Distill.Git;

namespace Distill.Reporting;

public static class CompactFailurePackFormatter
{
    public static string Format(
        VerificationStatus status,
        IReadOnlyList<CheckRunResult> checks,
        IReadOnlyList<RankedDiagnostic> rankedDiagnostics,
        DistillRunContext context,
        SufficiencyAssessment sufficiency,
        int maxDiagnostics = 20,
        SecretRedactor? redactor = null,
        GitChangeSnapshot? gitSnapshot = null)
    {
        redactor ??= new SecretRedactor();
        var lines = new List<string>
        {
            $"DISTILL VERIFY: {status.ToString().ToUpperInvariant()}",
            $"Profile: {context.Profile ?? "default"}",
            $"Run: {context.RunDirectory}"
        };

        if (gitSnapshot is { IsAvailable: false })
        {
            lines.Add("Git evidence: unavailable");
        }

        foreach (var check in checks)
        {
            lines.Add($"Check {check.CheckId} ({check.Kind}): {check.Status} exit={check.ExitCode?.ToString() ?? "n/a"} source={check.SourceId ?? "unknown"}");
            if (!string.IsNullOrWhiteSpace(check.ArtifactPointer))
            {
                lines.Add($"  Raw: {check.ArtifactPointer}");
            }
        }

        lines.Add(string.Empty);
        lines.Add("Primary diagnostics:");

        var errors = rankedDiagnostics
            .Where(item => item.Diagnostic.Severity == DiagnosticSeverity.Error)
            .Take(maxDiagnostics)
            .ToList();

        if (errors.Count == 0)
        {
            lines.Add("  (none captured)");
        }
        else
        {
            foreach (var item in errors)
            {
                lines.Add(FormatDiagnostic(item, redactor));
            }
        }

        if (sufficiency.Notes.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Sufficiency notes:");
            foreach (var note in sufficiency.Notes)
            {
                lines.Add($"  - {note}");
            }
        }

        if (sufficiency.RawExcerpts.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Raw excerpts:");
            foreach (var excerpt in sufficiency.RawExcerpts)
            {
                lines.Add($"[{excerpt.CheckId}] {excerpt.Reason}  {excerpt.ArtifactPath}");
                foreach (var line in excerpt.Lines)
                {
                    lines.Add($"  {redactor.Redact(line)}");
                }
            }
        }

        lines.Add(string.Empty);
        lines.Add($"Git diff: {RunArtifactLayout.GetGitDiffPath(context.RunDirectory)}");
        lines.Add($"Failure pack: {RunArtifactLayout.GetFailurePackPath(context.RunDirectory)}");

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatDiagnostic(RankedDiagnostic ranked, SecretRedactor redactor)
    {
        var diagnostic = ranked.Diagnostic;
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(diagnostic.TestName))
        {
            builder.Append(diagnostic.TestName).Append("  ");
        }

        if (!string.IsNullOrWhiteSpace(diagnostic.Code))
        {
            builder.Append(diagnostic.Code).Append("  ");
        }

        if (diagnostic.Location is not null)
        {
            builder.Append(diagnostic.Location.File).Append(':').Append(diagnostic.Location.Line).Append("  ");
        }

        builder.Append(redactor.Redact(diagnostic.Message));
        builder.Append("  [").Append(FormatProvenance(diagnostic.Provenance)).Append(", confidence=")
            .Append(diagnostic.Confidence.ToString("F2")).Append(']');

        if (ranked.Reasons.Count > 0)
        {
            builder.Append("  {").Append(string.Join(',', ranked.Reasons)).Append('}');
        }

        return builder.ToString();
    }

    private static string FormatProvenance(DiagnosticProvenance provenance)
        => provenance switch
        {
            DiagnosticProvenance.MsBuildBinaryLog => "msbuild-binlog",
            DiagnosticProvenance.VSTestLoggerEvent => "vstest-logger",
            DiagnosticProvenance.Trx => "trx",
            _ => provenance.ToString().ToLowerInvariant()
        };
}
