using Distill.Core.Diagnostics;
using Distill.Core.Evidence;
using Distill.Core.Runs;

namespace Distill.Reporting;

public static class BuildFailureFormatter
{
    public static string Format(BuildEvidence evidence, DistillRunContext context)
    {
        var errors = evidence.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        var status = evidence.Succeeded ? "PASSED" : "FAILED";
        var lines = new List<string>
        {
            $"DISTILL BUILD SPIKE: {status}",
            $"Errors: {errors.Count}"
        };

        foreach (var diagnostic in errors)
        {
            lines.Add(FormatDiagnosticLine(diagnostic));
        }

        if (errors.Count > 0)
        {
            var primary = errors[0];
            lines.Add($"Evidence: {FormatProvenance(primary.Provenance)} confidence={primary.Confidence:F2}");
        }

        if (evidence.BinlogPath is not null)
        {
            lines.Add($"Raw: {evidence.BinlogPath}");
        }

        lines.Add($"Run: {context.RunDirectory}");

        return string.Join(Environment.NewLine, lines);
    }

    public static VerificationStatus ResolveStatus(BuildEvidence evidence)
    {
        if (evidence.Succeeded)
        {
            return VerificationStatus.Pass;
        }

        var hasLowConfidenceOnly = evidence.Diagnostics.All(d => d.Confidence < 0.5);
        if (hasLowConfidenceOnly || evidence.Diagnostics.Count == 0)
        {
            return VerificationStatus.Uncertain;
        }

        return VerificationStatus.Fail;
    }

    private static string FormatDiagnosticLine(DistillDiagnostic diagnostic)
    {
        var code = diagnostic.Code ?? "ERROR";
        var location = diagnostic.Location is null
            ? "unknown"
            : $"{diagnostic.Location.File}:{diagnostic.Location.Line}";

        return $"{code}  {location}  {diagnostic.Message}";
    }

    private static string FormatProvenance(DiagnosticProvenance provenance)
        => provenance switch
        {
            DiagnosticProvenance.MsBuildBinaryLog => "msbuild-binlog",
            _ => provenance.ToString().ToLowerInvariant()
        };
}
