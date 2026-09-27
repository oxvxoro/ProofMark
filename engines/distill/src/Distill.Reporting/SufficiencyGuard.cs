using Distill.Core.Diagnostics;
using Distill.Core.Planning;
using Distill.Core.Runs;

namespace Distill.Reporting;

public sealed record SufficiencyAssessment(
    bool IsSufficient,
    bool ForceUncertain,
    IReadOnlyList<string> Notes,
    IReadOnlyList<RawExcerpt> RawExcerpts);

public static class SufficiencyGuard
{
    public static SufficiencyAssessment Assess(
        VerificationStatus status,
        IReadOnlyList<CheckRunResult> checks,
        IReadOnlyList<RankedDiagnostic> rankedDiagnostics,
        double minConfidence = 0.5)
    {
        if (status == VerificationStatus.Pass)
        {
            return new SufficiencyAssessment(
                true,
                false,
                Array.Empty<string>(),
                Array.Empty<RawExcerpt>());
        }

        var notes = new List<string>();
        if (checks.Any(check => check.Status == VerificationStatus.InfraError))
        {
            notes.Add("Execution infrastructure error; raw output is required.");
        }
        var primaryErrors = rankedDiagnostics
            .Where(item => item.Diagnostic.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (primaryErrors.Count == 0)
        {
            notes.Add("FAIL without primary structured diagnostics.");
        }

        var buildCheck = checks.FirstOrDefault(check => string.Equals(check.Kind, "build", StringComparison.OrdinalIgnoreCase));
        if (buildCheck?.Status == VerificationStatus.Fail &&
            buildCheck.Diagnostics.All(d => d.Kind != DiagnosticKind.Build))
        {
            notes.Add("Build failed but no build diagnostics were captured.");
            return new SufficiencyAssessment(
                false,
                true,
                notes,
                Array.Empty<RawExcerpt>());
        }

        var testCheck = checks.FirstOrDefault(check => string.Equals(check.Kind, "test", StringComparison.OrdinalIgnoreCase));
        if (testCheck?.Status == VerificationStatus.Fail &&
            testCheck.Diagnostics.All(d => string.IsNullOrWhiteSpace(d.TestName)))
        {
            notes.Add("Test failure without named failed test.");
        }

        if (primaryErrors.Any(item => item.Diagnostic.Confidence < minConfidence))
        {
            notes.Add("Primary diagnostic confidence below threshold.");
        }

        var primaryLowConfidence = primaryErrors.Any(item => item.Diagnostic.Confidence < minConfidence);
        var forceUncertain = notes.Any(note => note.Contains("without", StringComparison.OrdinalIgnoreCase))
            || primaryLowConfidence;
        return new SufficiencyAssessment(
            primaryErrors.Count > 0,
            forceUncertain,
            notes,
            Array.Empty<RawExcerpt>());
    }

    public static VerificationStatus Apply(VerificationStatus status, SufficiencyAssessment assessment)
    {
        if (status == VerificationStatus.Pass)
        {
            return status;
        }

        if (assessment.ForceUncertain || !assessment.IsSufficient)
        {
            return status == VerificationStatus.InfraError
                ? VerificationStatus.InfraError
                : VerificationStatus.Uncertain;
        }

        return status;
    }
}
