using Distill.Core.Diagnostics;
using Distill.Core.Evidence;
using Distill.Core.Runs;

namespace Distill.Reporting;

public static class TestFailureFormatter
{
    public static string Format(
        TestRunEvidence evidence,
        IReadOnlyList<DistillDiagnostic> diagnostics,
        DistillRunContext context,
        string sourceId)
    {
        var failedCases = evidence.Cases
            .Where(testCase => string.Equals(testCase.Outcome, "failed", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var status = evidence.Failed > 0 ? "FAILED" : "PASSED";
        var lines = new List<string>
        {
            $"DISTILL TEST SPIKE: {status}",
            $"Passed: {evidence.Passed}  Failed: {evidence.Failed}  Skipped: {evidence.Skipped}",
            $"Source: {sourceId}"
        };

        foreach (var diagnostic in diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
        {
            lines.Add(FormatDiagnosticLine(diagnostic));
        }

        if (diagnostics.Count > 0)
        {
            var primary = diagnostics[0];
            lines.Add($"Evidence: {FormatProvenance(primary.Provenance)} confidence={primary.Confidence:F2}");
        }

        if (string.Equals(sourceId, "vstest-trx", StringComparison.Ordinal))
        {
            lines.Add("Note: primary source fell back to TRX.");
        }

        lines.Add($"Events: {RunArtifactLayout.GetUnitEventsPath(context.RunDirectory)}");
        lines.Add($"TRX: {RunArtifactLayout.GetUnitTrxPath(context.RunDirectory)}");
        lines.Add($"Run: {context.RunDirectory}");

        if (failedCases.Count == 0 && evidence.Failed > 0)
        {
            lines.Add("Note: failed count reported but no failed case details were captured.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static VerificationStatus ResolveStatus(TestRunEvidence evidence)
    {
        if (evidence.Failed > 0)
        {
            var hasNamedFailure = evidence.Cases.Any(testCase =>
                string.Equals(testCase.Outcome, "failed", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(testCase.Name));

            return hasNamedFailure ? VerificationStatus.Fail : VerificationStatus.Uncertain;
        }

        if (evidence.Cases.Count == 0 && evidence.Passed == 0 && evidence.Skipped == 0)
        {
            return VerificationStatus.Uncertain;
        }

        return VerificationStatus.Pass;
    }

    private static string FormatDiagnosticLine(DistillDiagnostic diagnostic)
    {
        var testName = diagnostic.TestName ?? "unknown-test";
        var message = diagnostic.Message;
        return $"{testName}  {message}";
    }

    private static string FormatProvenance(DiagnosticProvenance provenance)
        => provenance switch
        {
            DiagnosticProvenance.VSTestLoggerEvent => "vstest-logger",
            DiagnosticProvenance.Trx => "trx",
            _ => provenance.ToString().ToLowerInvariant()
        };
}
