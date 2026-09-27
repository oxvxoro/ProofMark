using Distill.Execution;

namespace Distill.Tests.Analysis;

public sealed class ApiCompatReportGeneratorTests
{
    [Theory]
    [InlineData("proof.0.2.0.nupkg", "proof")]
    public void GuessPackageIdFromNupkgFileName_StripsVersion(string fileName, string expected)
    {
        Assert.Equal(expected, ApiCompatReportGenerator.GuessPackageIdFromNupkgFileName(fileName));
    }
}
