using Distill.Build.MSBuild;

namespace Distill.Tests.Build;

public class BuildCommandBuilderTests
{
    [Fact]
    public void BuildArguments_InjectsBinlogAndNologo()
    {
        var check = new Distill.Core.Planning.BuildCheckDefinition(
            Id: "build",
            Target: "App.sln",
            Arguments: ["--configuration", "Release"]);

        var arguments = BuildCommandBuilder.BuildArguments(check, @"C:\runs\build.binlog");

        Assert.Equal("build", arguments[0]);
        Assert.Equal("App.sln", arguments[1]);
        Assert.Contains("--configuration", arguments);
        Assert.Contains("--nologo", arguments);
        Assert.Contains("-bl:C:\\runs\\build.binlog", arguments);
    }
}
