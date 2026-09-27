using Distill.Core.Diagnostics;
using Distill.Reporting;

namespace Distill.Tests.Reporting;

public class FormatDiagnosticParserTests
{
    [Fact]
    public void FormatDiagnosticParser_MapsKnownDotnetFormatOutput()
    {
        var diagnostics = FormatDiagnosticParser.Parse(
        [
            "Would format src/Program.cs(12,4)",
            "  1 file would be formatted."
        ]);

        Assert.NotEmpty(diagnostics);
        Assert.Equal(DiagnosticKind.Format, diagnostics[0].Kind);
        Assert.Equal("src/Program.cs", diagnostics[0].Location?.File);
        Assert.Equal(12, diagnostics[0].Location?.Line);
        Assert.Equal(DiagnosticProvenance.KnownTextParser, diagnostics[0].Provenance);
    }
}
