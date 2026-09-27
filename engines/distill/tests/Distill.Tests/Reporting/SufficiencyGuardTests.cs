using Distill.Core.Diagnostics;
using Distill.Core.Planning;
using Distill.Core.Runs;
using Distill.Reporting;

namespace Distill.Tests.Reporting;

public class SufficiencyGuardTests
{
    [Fact]
    public void Assess_ConfidenceBelowThreshold_ForcesUncertain()
    {
        var diagnostic = DistillDiagnostic.Create(
            id: "low",
            kind: DiagnosticKind.Format,
            severity: DiagnosticSeverity.Error,
            source: "distill",
            code: "FORMAT",
            message: "format issue",
            provenance: DiagnosticProvenance.KnownTextParser,
            confidence: 0.4);

        var assessment = SufficiencyGuard.Assess(
            VerificationStatus.Fail,
            new[]
            {
                new CheckRunResult(
                    "format",
                    "format",
                    VerificationStatus.Fail,
                    1,
                    new[] { diagnostic },
                    "generic",
                    null,
                    TimeSpan.FromSeconds(1))
            },
            new[] { new RankedDiagnostic(diagnostic, 0, Array.Empty<string>()) },
            minConfidence: 0.5);

        Assert.True(assessment.ForceUncertain);
        Assert.Equal(VerificationStatus.Uncertain, SufficiencyGuard.Apply(VerificationStatus.Fail, assessment));
    }

    [Fact]
    public void Apply_InfraErrorWithLowConfidence_RemainsInfraError()
    {
        var diagnostic = DistillDiagnostic.Create(
            id: "infra",
            kind: DiagnosticKind.Infrastructure,
            severity: DiagnosticSeverity.Error,
            source: "distill",
            code: "INFRA",
            message: "infra",
            provenance: DiagnosticProvenance.RawFallback,
            confidence: 0.1);

        var assessment = SufficiencyGuard.Assess(
            VerificationStatus.InfraError,
            new[]
            {
                new CheckRunResult(
                    "unit",
                    "test",
                    VerificationStatus.InfraError,
                    null,
                    new[] { diagnostic },
                    "vstest-composite",
                    null,
                    TimeSpan.FromSeconds(1))
            },
            new[] { new RankedDiagnostic(diagnostic, 0, Array.Empty<string>()) },
            minConfidence: 0.5);

        Assert.Equal(VerificationStatus.InfraError, SufficiencyGuard.Apply(VerificationStatus.InfraError, assessment));
    }
}
