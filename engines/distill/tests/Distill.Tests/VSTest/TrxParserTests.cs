using Distill.Core.Diagnostics;
using Distill.Testing.VSTest;

namespace Distill.Tests.VSTest;

public class TrxParserTests
{
    private static string GetFixturePath(string fileName)
        => DistillFixturePath.Resolve("vstest", fileName);

    [Fact]
    public void Parse_SingleFailure_ExtractsTestNameAndMessage()
    {
        var parser = new TrxParser();
        var evidence = parser.Parse(GetFixturePath("single-failure.trx"));

        Assert.Equal(1, evidence.Failed);
        Assert.Contains(evidence.Cases, testCase => testCase.Name == "SampleTests.AlwaysFails");
    }

    [Fact]
    public void Parse_MultipleFailures_CountsAllFailedTests()
    {
        var parser = new TrxParser();
        var evidence = parser.Parse(GetFixturePath("multiple-failures.trx"));

        Assert.Equal(2, evidence.Failed);
        Assert.Equal(1, evidence.Passed);
    }

    [Fact]
    public void MapFailedCases_FromTrx_UsesTrxProvenance()
    {
        var parser = new TrxParser();
        var evidence = parser.Parse(GetFixturePath("single-failure.trx"));
        var diagnostics = VstestDiagnosticMapper.MapFailedCases(evidence.Cases, DiagnosticProvenance.Trx, 0.95);

        Assert.Single(diagnostics);
        Assert.Equal(DiagnosticProvenance.Trx, diagnostics[0].Provenance);
        Assert.Equal(0.95, diagnostics[0].Confidence);
    }
}
