using Distill.Core.Analysis;
using Distill.Core.Runs;

namespace Distill.Tests.Analysis;

public sealed class ApiCompatReportTests
{
    [Fact]
    public void ResolveStatus_Breaking_IsFail()
    {
        var projects = new[] { new ApiCompatProjectResult("App", "breaking", "removed type") };
        Assert.Equal(VerificationStatus.Fail, ApiCompatReport.ResolveStatus(projects));
    }

    [Fact]
    public void ResolveStatus_Inconclusive_IsUncertain()
    {
        var projects = new[] { new ApiCompatProjectResult("App", "inconclusive", "no baseline") };
        Assert.Equal(VerificationStatus.Uncertain, ApiCompatReport.ResolveStatus(projects));
    }

    [Fact]
    public void ResolveStatus_Additive_IsPass()
    {
        var projects = new[] { new ApiCompatProjectResult("App", "additive", null) };
        Assert.Equal(VerificationStatus.Pass, ApiCompatReport.ResolveStatus(projects));
    }

    [Fact]
    public void TryRead_MissingFile_ReturnsFalse()
    {
        Assert.False(ApiCompatReport.TryRead(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"), out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void TryRead_ValidReport_ParsesProjects()
    {
        var path = Path.Combine(Path.GetTempPath(), "apicompat-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, """{"projects":[{"project":"Proof.Core","status":"pass"}]}""");
        try
        {
            Assert.True(ApiCompatReport.TryRead(path, out var projects, out _));
            Assert.Equal("Proof.Core", projects[0].Project);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
