using Distill.Core.Config;
using Distill.Core.Planning;
using Distill.Git;
using Distill.Reporting;

namespace Distill.Tests.Core;

public class DotnetCommandParserTests
{
    [Fact]
    public void Parse_StripsDotnetPrefixAndTarget()
    {
        var parsed = DotnetCommandParser.Parse("dotnet build MyApp.sln --no-restore");

        Assert.Equal("build", parsed.Verb);
        Assert.Equal("MyApp.sln", parsed.Target);
        Assert.Equal(["--no-restore"], parsed.Arguments);
    }

    [Fact]
    public void ToArgumentList_RebuildsCommandTokens()
    {
        var parsed = new DotnetCommandParser.ParsedDotnetCommand("test", "tests/Unit/Unit.csproj", ["--no-build"]);
        var args = DotnetCommandParser.ToArgumentList(parsed);

        Assert.Equal(["test", "tests/Unit/Unit.csproj", "--no-build"], args);
    }
}

