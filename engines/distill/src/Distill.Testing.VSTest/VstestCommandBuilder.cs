using Distill.Core.Paths;
using Distill.Testing.Abstractions;

namespace Distill.Testing.VSTest;

public static class VstestCommandBuilder
{
    public static IReadOnlyList<string> BuildLoggerArguments(
        TestCheckDefinition check,
        string extensionDirectory,
        string eventsPath)
    {
        var arguments = new List<string> { "test" };

        if (!string.IsNullOrWhiteSpace(check.Target))
        {
            arguments.Add(check.Target);
        }

        foreach (var argument in check.Arguments)
        {
            arguments.Add(argument);
        }

        arguments.Add("--nologo");
        arguments.Add("--test-adapter-path");
        arguments.Add(NormalizeLoggerPath(extensionDirectory));
        arguments.Add("--logger");
        arguments.Add($"distill;Output={NormalizeLoggerPath(eventsPath)}");

        return arguments;
    }

    public static IReadOnlyList<string> BuildDualLoggerArguments(
        TestCheckDefinition check,
        string extensionDirectory,
        string eventsPath,
        string trxPath)
    {
        var arguments = new List<string> { "test" };

        if (!string.IsNullOrWhiteSpace(check.Target))
        {
            arguments.Add(check.Target);
        }

        foreach (var argument in check.Arguments)
        {
            arguments.Add(argument);
        }

        arguments.Add("--nologo");
        arguments.Add("--test-adapter-path");
        arguments.Add(NormalizeLoggerPath(extensionDirectory));
        arguments.Add("--logger");
        arguments.Add($"distill;Output={NormalizeLoggerPath(eventsPath)}");
        arguments.Add("--logger");
        arguments.Add($"trx;LogFileName={NormalizeLoggerPath(trxPath)}");

        return arguments;
    }

    public static IReadOnlyList<string> BuildTrxArguments(
        TestCheckDefinition check,
        string trxPath)
    {
        var arguments = new List<string> { "test" };

        if (!string.IsNullOrWhiteSpace(check.Target))
        {
            arguments.Add(check.Target);
        }

        foreach (var argument in check.Arguments)
        {
            arguments.Add(argument);
        }

        arguments.Add("--nologo");
        arguments.Add("--logger");
        arguments.Add($"trx;LogFileName={NormalizeLoggerPath(trxPath)}");

        return arguments;
    }

    private static string NormalizeLoggerPath(string path)
        => DistillPath.ForCommandArgument(path);
}
