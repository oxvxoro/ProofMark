using Distill.Core.Planning;

namespace Distill.Build.MSBuild;

public static class BuildCommandBuilder
{
    public static IReadOnlyList<string> BuildArguments(
        BuildCheckDefinition check,
        string binlogPath)
    {
        var arguments = new List<string> { "build" };

        if (!string.IsNullOrWhiteSpace(check.Target))
        {
            arguments.Add(check.Target);
        }

        foreach (var argument in check.Arguments)
        {
            arguments.Add(argument);
        }

        arguments.Add("--nologo");
        arguments.Add($"-bl:{binlogPath}");

        return arguments;
    }
}
