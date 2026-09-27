using Distill.Core.Diagnostics;
using Distill.Core.Planning;
using Distill.Core.Redaction;
using Distill.Core.Runs;
using Distill.Reporting;

namespace Distill.Tests;

public class JsonVerificationReporterTests
{
    [Fact]
    public void Format_RedactsSecretsInRawExcerptLines()
    {
        var context = new DistillRunContext
        {
            RunId = "d-20260101-000000-abcdef",
            WorkspaceRoot = "/repo",
            RunDirectory = "/repo/.distill/runs/d-20260101-000000-abcdef",
            Profile = "quick"
        };

        var sufficiency = new SufficiencyAssessment(
            false,
            true,
            ["Build failed but no build diagnostics were captured."],
            [new RawExcerpt("build", "/repo/.distill/runs/x/build/stdout.log", ["token=SECRET123 more text"], "INSUFFICIENT STRUCTURED EVIDENCE")]);

        var json = JsonVerificationReporter.Format(
            VerificationStatus.Uncertain,
            context,
            Array.Empty<CheckRunResult>(),
            Array.Empty<RankedDiagnostic>(),
            sufficiency,
            new SecretRedactor());

        Assert.DoesNotContain("SECRET123", json);
        Assert.Contains("token=***", json);
    }

    [Fact]
    public void Format_AppliesCustomRedactionPatternToRawExcerptLines()
    {
        var context = new DistillRunContext
        {
            RunId = "d-20260101-000000-abcdef",
            WorkspaceRoot = "/repo",
            RunDirectory = "/repo/.distill/runs/d-20260101-000000-abcdef",
            Profile = "quick"
        };

        var sufficiency = new SufficiencyAssessment(
            false,
            true,
            ["note"],
            [new RawExcerpt("build", "/path", ["CUSTOM-ID-98765"], "INSUFFICIENT STRUCTURED EVIDENCE")]);

        var json = JsonVerificationReporter.Format(
            VerificationStatus.Uncertain,
            context,
            Array.Empty<CheckRunResult>(),
            Array.Empty<RankedDiagnostic>(),
            sufficiency,
            new SecretRedactor(["CUSTOM-ID-\\d+"]));

        Assert.DoesNotContain("CUSTOM-ID-98765", json);
    }

    [Fact]
    public void Format_PreservesRawExcerptMetadataFields()
    {
        var context = new DistillRunContext
        {
            RunId = "d-20260101-000000-abcdef",
            WorkspaceRoot = "/repo",
            RunDirectory = "/repo/.distill/runs/d-20260101-000000-abcdef",
            Profile = "quick"
        };

        var sufficiency = new SufficiencyAssessment(
            false,
            true,
            ["note"],
            [new RawExcerpt("unit", "/repo/.distill/runs/x/unit/stderr.log", ["plain line"], "LOW CONFIDENCE")]);

        var json = JsonVerificationReporter.Format(
            VerificationStatus.Uncertain,
            context,
            Array.Empty<CheckRunResult>(),
            Array.Empty<RankedDiagnostic>(),
            sufficiency,
            new SecretRedactor());

        Assert.Contains("\"unit\"", json);
        Assert.Contains("stderr.log", json);
        Assert.Contains("LOW CONFIDENCE", json);
        Assert.Contains("plain line", json);
    }
}
