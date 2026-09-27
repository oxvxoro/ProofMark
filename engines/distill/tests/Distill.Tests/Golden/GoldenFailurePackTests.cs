using Distill.Core.Abstractions;
using Distill.Core.Diagnostics;
using Distill.Core.Planning;
using Distill.Core.Runs;
using Distill.Git;
using Distill.Reporting;
using Distill.Testing.VSTest;
using Distill.Tests.Golden;

namespace Distill.Tests;

public class GoldenFailurePackTests
{
    private const string GoldenRunId = "d-golden-scenario";
    private const string GoldenRunDirectory = ".distill/runs/d-golden-scenario";

    [Fact]
    public void BuildFailure_MatchesGoldenFailurePack()
    {
        const string changedFile = "src/App.cs";
        var diagnostic = DistillDiagnostic.Create(
            id: "build-error-1",
            kind: DiagnosticKind.Build,
            severity: DiagnosticSeverity.Error,
            source: "build",
            code: "CS1002",
            message: "; expected",
            location: new SourceLocation(changedFile, 42, 9),
            provenance: DiagnosticProvenance.MsBuildBinaryLog,
            confidence: 1.0);

        var checks = new[]
        {
            new CheckRunResult(
                "build",
                "build",
                VerificationStatus.Fail,
                1,
                new[] { diagnostic },
                "msbuild-binlog",
                $"{GoldenRunDirectory}/build/build.binlog",
                TimeSpan.FromSeconds(12))
        };

        var gitPatch = """
            diff --git a/src/App.cs b/src/App.cs
            --- a/src/App.cs
            +++ b/src/App.cs
            @@ -40,3 +40,4 @@
             var ok = true
            -return ok
            +return ok;
            """;

        var gitSnapshot = new GitChangeSnapshot(
            $" M {changedFile}",
            new[] { changedFile },
            DiffHunkParser.Parse(gitPatch),
            gitPatch);

        var pack = BuildPack(VerificationStatus.Fail, "quick", checks, gitSnapshot);
        GoldenText.AssertMatchesGolden(pack.CompactText, GoldenPaths.GetExpectedFailurePackPath("build-failure"));
    }

    [Fact]
    public void TestFailure_MatchesGoldenFailurePack()
    {
        var eventsPath = Path.Combine(GoldenPaths.RepoRoot, "fixtures", "vstest", "failed.events.jsonl");
        var parseResult = new JsonlTestResultParser().Parse(eventsPath);
        Assert.True(parseResult.IsComplete);
        Assert.NotNull(parseResult.Evidence);

        var diagnostics = VstestCompositeResultSource.ResolveDiagnostics(parseResult.Evidence, "vstest-logger");
        var checks = new[]
        {
            new CheckRunResult(
                "build",
                "build",
                VerificationStatus.Pass,
                0,
                Array.Empty<DistillDiagnostic>(),
                "msbuild-binlog",
                $"{GoldenRunDirectory}/build/build.binlog",
                TimeSpan.FromSeconds(8)),
            new CheckRunResult(
                "unit",
                "test",
                VerificationStatus.Fail,
                1,
                diagnostics,
                "vstest-logger",
                $"{GoldenRunDirectory}/unit/tests.events.jsonl",
                TimeSpan.FromSeconds(5))
        };

        const string changedFile = "SampleTests.cs";
        var gitPatch = """
            diff --git a/SampleTests.cs b/SampleTests.cs
            --- a/SampleTests.cs
            +++ b/SampleTests.cs
            @@ -8,3 +8,3 @@
             public void AlwaysFails()
             {
            -    Assert.True(false);
            +    Assert.True(true);
             }
            """;

        var gitSnapshot = new GitChangeSnapshot(
            $" M {changedFile}",
            new[] { changedFile },
            DiffHunkParser.Parse(gitPatch),
            gitPatch);

        var pack = BuildPack(VerificationStatus.Fail, "quick", checks, gitSnapshot);
        GoldenText.AssertMatchesGolden(pack.CompactText, GoldenPaths.GetExpectedFailurePackPath("test-failure"));
    }

    [Fact]
    public void InsufficientFailure_IncludesRawExcerptAndBecomesUncertain()
    {
        var checks = new[]
        {
            new CheckRunResult(
                "unit",
                "test",
                VerificationStatus.Fail,
                1,
                Array.Empty<DistillDiagnostic>(),
                "generic",
                Path.Combine(
                    GoldenPaths.GoldenRoot,
                    "insufficient-fail",
                    "raw.log"),
                TimeSpan.FromSeconds(2))
        };

        var context = new DistillRunContext
        {
            RunId = GoldenRunId,
            WorkspaceRoot = "/repo",
            RunDirectory = GoldenRunDirectory,
            Profile = "quick"
        };

        var pack = VerificationReportBuilder.Build(
            VerificationStatus.Fail,
            context,
            checks,
            new GitChangeSnapshot(
                string.Empty,
                Array.Empty<string>(),
                Array.Empty<ChangedHunk>(),
                string.Empty),
            new ReportOptions(ReportOutputFormat.Compact));

        Assert.Equal(VerificationStatus.Uncertain, pack.Status);
        GoldenText.AssertMatchesGolden(
            pack.CompactText,
            GoldenPaths.GetExpectedFailurePackPath("insufficient-fail"));
    }

    [Fact]
    public void AnalysisFailure_MatchesGoldenFailurePack()
    {
        var diagnostic = DistillDiagnostic.Create(
            id: "sarif-1",
            kind: DiagnosticKind.Analysis,
            severity: DiagnosticSeverity.Error,
            source: "DemoAnalyzer",
            code: "DEMO002",
            message: "Unsafe API usage",
            location: new SourceLocation("src/Demo.cs", 12, 5),
            provenance: DiagnosticProvenance.Sarif,
            confidence: 1.0);
        var checks = new[]
        {
            new CheckRunResult(
                "analysis",
                "analysis",
                VerificationStatus.Fail,
                1,
                new[] { diagnostic },
                "sarif",
                $"{GoldenRunDirectory}/analysis/analysis.sarif",
                TimeSpan.FromSeconds(3))
        };

        var pack = BuildPack(
            VerificationStatus.Fail,
            "full",
            checks,
            new GitChangeSnapshot(
                string.Empty,
                new[] { "src/Demo.cs" },
                Array.Empty<ChangedHunk>(),
                string.Empty));

        GoldenText.AssertMatchesGolden(
            pack.CompactText,
            GoldenPaths.GetExpectedFailurePackPath("analysis-failure"));
    }

    [Fact]
    public void RedactionFailure_MatchesGoldenFailurePackWithoutSecret()
    {
        var diagnostic = DistillDiagnostic.Create(
            id: "analysis-secret",
            kind: DiagnosticKind.Analysis,
            severity: DiagnosticSeverity.Error,
            source: "DemoAnalyzer",
            code: "DEMO003",
            message: "password=hunter2 Bearer abc123",
            provenance: DiagnosticProvenance.Sarif,
            confidence: 1.0);
        var checks = new[]
        {
            new CheckRunResult(
                "analysis",
                "analysis",
                VerificationStatus.Fail,
                1,
                new[] { diagnostic },
                "sarif",
                $"{GoldenRunDirectory}/analysis/analysis.sarif",
                TimeSpan.FromSeconds(1))
        };
        var context = new DistillRunContext
        {
            RunId = GoldenRunId,
            WorkspaceRoot = "/repo",
            RunDirectory = GoldenRunDirectory,
            Profile = "full"
        };

        var pack = VerificationReportBuilder.Build(
            VerificationStatus.Fail,
            context,
            checks,
            new GitChangeSnapshot(
                string.Empty,
                Array.Empty<string>(),
                Array.Empty<ChangedHunk>(),
                string.Empty),
            new ReportOptions(ReportOutputFormat.Compact));

        Assert.DoesNotContain("hunter2", pack.CompactText);
        Assert.DoesNotContain("abc123", pack.CompactText);
        GoldenText.AssertMatchesGolden(
            pack.CompactText,
            GoldenPaths.GetExpectedFailurePackPath("redaction"));
    }

    private static VerificationPack BuildPack(
        VerificationStatus rawStatus,
        string profile,
        IReadOnlyList<CheckRunResult> checks,
        GitChangeSnapshot gitSnapshot)
    {
        var context = new DistillRunContext
        {
            RunId = GoldenRunId,
            WorkspaceRoot = "/repo",
            RunDirectory = GoldenRunDirectory,
            Profile = profile
        };

        return VerificationReportBuilder.Build(
            rawStatus,
            context,
            checks,
            gitSnapshot,
            new ReportOptions(ReportOutputFormat.Compact));
    }
}
