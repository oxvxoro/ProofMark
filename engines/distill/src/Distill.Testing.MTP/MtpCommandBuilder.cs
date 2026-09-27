using Distill.Core.Paths;
using Distill.Testing.Abstractions;

namespace Distill.Testing.MTP;

public static class MtpCommandBuilder
{
    public static IReadOnlyList<string> BuildReportArguments(
        TestCheckDefinition check,
        string resultsDirectory,
        string trxFileName = "fallback.trx")
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

        arguments.Add("--results-directory");
        arguments.Add(NormalizePath(resultsDirectory));
        arguments.Add("--report-trx");
        arguments.Add("--report-trx-filename");
        arguments.Add(trxFileName);

        return arguments;
    }

    private static string NormalizePath(string path)
        => DistillPath.ForCommandArgument(path);
}
