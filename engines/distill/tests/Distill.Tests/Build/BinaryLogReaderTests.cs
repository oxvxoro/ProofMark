using Distill.Build.MSBuild;
using Distill.Core.Diagnostics;

namespace Distill.Tests.Build;

public class BinaryLogReaderTests
{
    private static string GetFixturePath(string fileName)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "fixtures", "binlog", fileName));

    [Fact]
    public void Read_SuccessBinlog_HasNoErrors()
    {
        var reader = new BinaryLogReader();
        var result = reader.Read(GetFixturePath("success.binlog"));

        Assert.Empty(result.Errors);
        Assert.True(result.BuildSucceeded || result.Errors.Count == 0);
    }

    [Fact]
    public void Read_Cs0246Binlog_ExtractsCompilerError()
    {
        var reader = new BinaryLogReader();
        var result = reader.Read(GetFixturePath("cs0246.binlog"));

        Assert.False(result.BuildSucceeded);
        Assert.Contains(result.Errors, e =>
            e.Code == "CS0246" &&
            e.Location?.File.Contains("Broken.cs", StringComparison.OrdinalIgnoreCase) == true);
        Assert.All(result.Errors, e => Assert.Equal(DiagnosticProvenance.MsBuildBinaryLog, e.Provenance));
    }

    [Fact]
    public void Read_MultipleErrorsBinlog_ExtractsBothErrors()
    {
        var result = new BinaryLogReader().Read(GetFixturePath("multiple-errors.binlog"));

        Assert.True(result.Errors.Count >= 2);
        Assert.Contains(result.Errors, error => error.Code == "CS0246");
    }

    [Fact]
    public void Read_WarningOnlyBinlog_ExtractsWarningWithoutError()
    {
        var result = new BinaryLogReader().Read(GetFixturePath("warning-only.binlog"));

        Assert.Empty(result.Errors);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Read_MultiProjectBinlog_IdentifiesFailingProject()
    {
        var result = new BinaryLogReader().Read(GetFixturePath("multi-project.binlog"));

        Assert.Contains(result.Errors, error =>
            error.Project?.Contains("MultiBad", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void Read_SuccessBinlog_CollectsBuiltProjects()
    {
        var result = new BinaryLogReader().Read(GetFixturePath("success.binlog"));

        Assert.NotNull(result.BuiltProjects);
        Assert.NotEmpty(result.BuiltProjects);
        Assert.Contains(result.BuiltProjects, project => project.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
    }
}
