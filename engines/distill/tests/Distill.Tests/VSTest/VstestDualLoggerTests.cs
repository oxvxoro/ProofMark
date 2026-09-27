using Distill.Testing.Abstractions;
using Distill.Testing.VSTest;

namespace Distill.Tests.VSTest;

public class VstestDualLoggerTests
{
    [Fact]
    public void BuildDualLoggerArguments_IncludesDistillAndTrxLoggers()
    {
        var check = new TestCheckDefinition(
            Id: "unit",
            Target: "App.Tests.csproj",
            Arguments: Array.Empty<string>());

        var arguments = VstestCommandBuilder.BuildDualLoggerArguments(
            check,
            @"C:\extensions",
            @"C:\runs\unit\tests.events.jsonl",
            @"C:\runs\unit\fallback.trx");

        Assert.Contains("distill;Output=C:/runs/unit/tests.events.jsonl", arguments);
        Assert.Contains("trx;LogFileName=C:/runs/unit/fallback.trx", arguments);
        Assert.Equal(2, arguments.Count(argument => argument == "--logger"));
    }
}
