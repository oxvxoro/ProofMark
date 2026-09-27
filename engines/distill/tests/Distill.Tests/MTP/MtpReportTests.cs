using Distill.Testing.Abstractions;
using Distill.Testing.MTP;
using Distill.Testing.VSTest;

namespace Distill.Tests.MTP;

public class MtpCommandBuilderTests
{
    [Fact]
    public void BuildReportArguments_IncludesTrxReportOptions()
    {
        var check = new TestCheckDefinition(
            Id: "unit",
            Target: "App.Tests.csproj",
            Arguments: ["--no-build"]);

        var arguments = MtpCommandBuilder.BuildReportArguments(
            check,
            @"C:\runs\unit",
            "fallback.trx");

        Assert.Equal(
            [
                "test",
                "App.Tests.csproj",
                "--no-build",
                "--results-directory",
                "C:/runs/unit",
                "--report-trx",
                "--report-trx-filename",
                "fallback.trx"
            ],
            arguments);
    }
}

public class MtpReportParserTests
{
    [Fact]
    public void Parse_FailedTrxFixture_ProducesFailedCase()
    {
        var trxPath = DistillFixturePath.Resolve("mtp", "failed.trx");
        var parsed = new TrxParser().Parse(trxPath, "mtp-report");

        Assert.Equal(1, parsed.Failed);
        Assert.Contains(parsed.Cases, testCase => testCase.Name.Contains("AlwaysFails", StringComparison.Ordinal));
    }

    [Fact]
    public void ResolveDiagnostics_UsesMtpStructuredReportProvenance()
    {
        var trxPath = DistillFixturePath.Resolve("mtp", "failed.trx");
        var evidence = new TrxParser().Parse(trxPath, "mtp-report");
        var diagnostics = MtpReportResultSource.ResolveDiagnostics(evidence);

        Assert.NotEmpty(diagnostics);
        Assert.All(
            diagnostics,
            diagnostic => Assert.Equal(
                Distill.Core.Diagnostics.DiagnosticProvenance.MtpStructuredReport,
                diagnostic.Provenance));
    }
}
