using Distill.Core.Runs;
using System.CommandLine;

namespace Distill.Cli.Commands;

public static class ReportCommand
{
    public static Command Create()
    {
        var runOption = new Option<string?>("--run")
        {
            Description = "Run id to inspect. Defaults to latest run."
        };

        var command = new Command("report", "Show the Failure Pack from a verification run.")
        {
            runOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var runId = parseResult.GetValue(runOption);
            return await ExecuteAsync(runId, cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    internal static async Task<int> ExecuteAsync(string? runId, CancellationToken cancellationToken)
    {
        var workspaceRoot = Directory.GetCurrentDirectory();
        var resolved = await RunContextResolver.ResolveLatestAsync(workspaceRoot, runId, cancellationToken)
            .ConfigureAwait(false);

        if (resolved is null)
        {
            Console.Error.WriteLine("No verification run found.");
            return ExitCodeMapper.ConfigError;
        }

        var failurePackPath = RunArtifactLayout.GetFailurePackPath(resolved.Value.RunDirectory);
        if (!File.Exists(failurePackPath))
        {
            Console.Error.WriteLine($"Failure pack not found: {failurePackPath}");
            return ExitCodeMapper.FromStatus(VerificationStatus.InfraError);
        }

        Console.WriteLine(await File.ReadAllTextAsync(failurePackPath, cancellationToken).ConfigureAwait(false));
        return 0;
    }
}
