using Distill.Testing.VSTest;

namespace Distill.Tests.VSTest;

public class VstestCommandBuilderTests
{
    [Fact]
    public void BuildLoggerArguments_UsesForwardSlashesForWindowsPaths()
    {
        var check = new Distill.Testing.Abstractions.TestCheckDefinition(
            Id: "unit",
            Target: "App.Tests.csproj",
            Arguments: Array.Empty<string>());

        var arguments = VstestCommandBuilder.BuildLoggerArguments(
            check,
            @"C:\extensions",
            @"C:\runs\unit\tests.events.jsonl");

        var loggerArgument = arguments[^1];
        Assert.Contains("Output=C:/runs/unit/tests.events.jsonl", loggerArgument);
        Assert.DoesNotContain(@"Output=C:\runs", loggerArgument);
    }
}
