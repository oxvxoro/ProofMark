using Distill.Core.Diagnostics;
using Distill.Core.Planning;
using Distill.Core.Runs;
using Distill.Reporting;

namespace Distill.Tests.Reporting;

public class RawExcerptCollectorTests
{
    [Fact]
    public void Collect_SufficientPassRun_ReturnsNoExcerpts()
    {
        var check = new CheckRunResult(
            "build",
            "build",
            VerificationStatus.Pass,
            0,
            Array.Empty<DistillDiagnostic>(),
            "msbuild-binlog",
            null,
            TimeSpan.FromSeconds(1));

        var assessment = new SufficiencyAssessment(true, false, Array.Empty<string>(), Array.Empty<RawExcerpt>());

        var excerpts = RawExcerptCollector.Collect([check], assessment);

        Assert.Empty(excerpts);
    }

    [Fact]
    public void Collect_PassCheckAlongsideInsufficientFailingCheck_OnlyFailingCheckEmitsExcerpt()
    {
        var tempDirectory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var passLogPath = Path.Combine(tempDirectory, "pass-stdout.log");
            File.WriteAllText(passLogPath, "pass check output\n");

            var failLogPath = Path.Combine(tempDirectory, "fail-stdout.log");
            File.WriteAllText(failLogPath, "fail check output\n");

            var passCheck = new CheckRunResult(
                "build",
                "build",
                VerificationStatus.Pass,
                0,
                Array.Empty<DistillDiagnostic>(),
                "msbuild-binlog",
                passLogPath,
                TimeSpan.FromSeconds(1));

            var failCheck = new CheckRunResult(
                "unit",
                "test",
                VerificationStatus.Fail,
                1,
                Array.Empty<DistillDiagnostic>(),
                "generic",
                failLogPath,
                TimeSpan.FromSeconds(1));

            var assessment = new SufficiencyAssessment(
                false,
                true,
                ["Test failure without named failed test."],
                Array.Empty<RawExcerpt>());

            var excerpts = RawExcerptCollector.Collect([passCheck, failCheck], assessment);

            var excerpt = Assert.Single(excerpts);
            Assert.Equal("unit", excerpt.CheckId);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Collect_UncertainCheck_RemainsEligible()
    {
        var tempDirectory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var logPath = Path.Combine(tempDirectory, "stdout.log");
            File.WriteAllText(logPath, "uncertain output\n");

            var check = new CheckRunResult(
                "format",
                "format",
                VerificationStatus.Uncertain,
                1,
                Array.Empty<DistillDiagnostic>(),
                "generic",
                logPath,
                TimeSpan.FromSeconds(1));

            var assessment = new SufficiencyAssessment(
                false,
                true,
                ["Primary diagnostic confidence below threshold."],
                Array.Empty<RawExcerpt>());

            var excerpts = RawExcerptCollector.Collect([check], assessment);

            Assert.Single(excerpts);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Collect_InfraErrorCheck_RemainsEligible()
    {
        var tempDirectory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var logPath = Path.Combine(tempDirectory, "stdout.log");
            File.WriteAllText(logPath, "infra output\n");

            var check = new CheckRunResult(
                "unit",
                "test",
                VerificationStatus.InfraError,
                null,
                Array.Empty<DistillDiagnostic>(),
                "vstest-composite",
                logPath,
                TimeSpan.FromSeconds(1));

            var assessment = new SufficiencyAssessment(
                false,
                true,
                ["Execution infrastructure error; raw output is required."],
                Array.Empty<RawExcerpt>());

            var excerpts = RawExcerptCollector.Collect([check], assessment);

            Assert.Single(excerpts);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Collect_DefaultMaxLines_ReturnsLastFortyLines()
    {
        var tempDirectory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var logPath = Path.Combine(tempDirectory, "stdout.log");
            File.WriteAllLines(logPath, Enumerable.Range(1, 100).Select(i => $"line {i}"));

            var check = new CheckRunResult(
                "unit",
                "test",
                VerificationStatus.Fail,
                1,
                Array.Empty<DistillDiagnostic>(),
                "generic",
                logPath,
                TimeSpan.FromSeconds(1));

            var assessment = new SufficiencyAssessment(
                false,
                true,
                ["FAIL without primary structured diagnostics."],
                Array.Empty<RawExcerpt>());

            var excerpts = RawExcerptCollector.Collect([check], assessment);

            var excerpt = Assert.Single(excerpts);
            Assert.Equal(40, excerpt.Lines.Count);
            Assert.Equal("line 61", excerpt.Lines[0]);
            Assert.Equal("line 100", excerpt.Lines[^1]);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Collect_ExplicitMaxLines_HonorsOverride()
    {
        var tempDirectory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var logPath = Path.Combine(tempDirectory, "stdout.log");
            File.WriteAllLines(logPath, Enumerable.Range(1, 100).Select(i => $"line {i}"));

            var check = new CheckRunResult(
                "unit",
                "test",
                VerificationStatus.Fail,
                1,
                Array.Empty<DistillDiagnostic>(),
                "generic",
                logPath,
                TimeSpan.FromSeconds(1));

            var assessment = new SufficiencyAssessment(
                false,
                true,
                ["FAIL without primary structured diagnostics."],
                Array.Empty<RawExcerpt>());

            var excerpts = RawExcerptCollector.Collect([check], assessment, maxLines: 80);

            var excerpt = Assert.Single(excerpts);
            Assert.Equal(80, excerpt.Lines.Count);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Collect_BinlogArtifact_IsExcludedAsBinary()
    {
        var tempDirectory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var binlogPath = Path.Combine(tempDirectory, "build.binlog");
            File.WriteAllText(binlogPath, "not really binary but excluded by extension");

            var check = new CheckRunResult(
                "build",
                "build",
                VerificationStatus.Fail,
                1,
                Array.Empty<DistillDiagnostic>(),
                "msbuild-binlog",
                binlogPath,
                TimeSpan.FromSeconds(1));

            var assessment = new SufficiencyAssessment(
                false,
                true,
                ["Build failed but no build diagnostics were captured."],
                Array.Empty<RawExcerpt>());

            var excerpts = RawExcerptCollector.Collect([check], assessment);

            Assert.Empty(excerpts);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Collect_MissingArtifact_DoesNotThrow()
    {
        var check = new CheckRunResult(
            "unit",
            "test",
            VerificationStatus.Fail,
            1,
            Array.Empty<DistillDiagnostic>(),
            "generic",
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.log"),
            TimeSpan.FromSeconds(1));

        var assessment = new SufficiencyAssessment(
            false,
            true,
            ["FAIL without primary structured diagnostics."],
            Array.Empty<RawExcerpt>());

        var excerpts = RawExcerptCollector.Collect([check], assessment);

        Assert.Empty(excerpts);
    }
}
