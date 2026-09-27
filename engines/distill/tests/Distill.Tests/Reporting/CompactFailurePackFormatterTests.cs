using Distill.Core.Diagnostics;
using Distill.Core.Planning;
using Distill.Core.Redaction;
using Distill.Core.Runs;
using Distill.Reporting;

namespace Distill.Tests.Reporting;

public class CompactFailurePackFormatterTests
{
    private static readonly DistillRunContext Context = new()
    {
        RunId = "d-test-run",
        WorkspaceRoot = "/repo",
        RunDirectory = ".distill/runs/d-test-run",
        Profile = "quick"
    };

    private static readonly SufficiencyAssessment EmptySufficiency = new(
        true,
        false,
        Array.Empty<string>(),
        Array.Empty<RawExcerpt>());

    private static IReadOnlyList<RankedDiagnostic> CreateErrors(int count)
        => Enumerable.Range(0, count)
            .Select(index => new RankedDiagnostic(
                DistillDiagnostic.Create(
                    id: $"d{index}",
                    kind: DiagnosticKind.Build,
                    severity: DiagnosticSeverity.Error,
                    source: "build",
                    code: $"CS{1000 + index}",
                    message: $"error {index}",
                    provenance: DiagnosticProvenance.MsBuildBinaryLog,
                    confidence: 1.0),
                0,
                Array.Empty<string>()))
            .ToList();

    [Fact]
    public void Format_NineDistinctErrorsWithDefaultBudget_RendersOnlyEight()
    {
        var text = CompactFailurePackFormatter.Format(
            VerificationStatus.Fail,
            Array.Empty<CheckRunResult>(),
            CreateErrors(9),
            Context,
            EmptySufficiency,
            maxDiagnostics: 8);

        for (var index = 0; index < 8; index++)
        {
            Assert.Contains($"error {index}", text);
        }

        Assert.DoesNotContain("error 8", text);
    }

    [Fact]
    public void Format_ExplicitHigherMaxDiagnostics_RendersAllAvailable()
    {
        var text = CompactFailurePackFormatter.Format(
            VerificationStatus.Fail,
            Array.Empty<CheckRunResult>(),
            CreateErrors(9),
            Context,
            EmptySufficiency,
            maxDiagnostics: 20);

        for (var index = 0; index < 9; index++)
        {
            Assert.Contains($"error {index}", text);
        }
    }

    [Fact]
    public void Format_RawExcerptContainingSecret_RedactsLineInCompactOutput()
    {
        var sufficiency = new SufficiencyAssessment(
            false,
            true,
            ["FAIL without primary structured diagnostics."],
            [new RawExcerpt("build", "/repo/.distill/runs/x/build/stdout.log", ["token=SECRET123 more text"], "INSUFFICIENT STRUCTURED EVIDENCE")]);

        var text = CompactFailurePackFormatter.Format(
            VerificationStatus.Uncertain,
            Array.Empty<CheckRunResult>(),
            Array.Empty<RankedDiagnostic>(),
            Context,
            sufficiency,
            maxDiagnostics: 8,
            redactor: new SecretRedactor());

        Assert.DoesNotContain("SECRET123", text);
        Assert.Contains("token=***", text);
    }
}
