using System.CommandLine;

namespace Distill.Cli.Commands;

public static class ArtifactsCommand
{
    public static Command Create()
    {
        var runOption = new Option<string?>("--run")
        {
            Description = "Run id to inspect. Defaults to latest run."
        };

        var command = new Command("artifacts", "List artifact files for a verification run.")
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

        var files = Directory
            .EnumerateFiles(resolved.Value.RunDirectory, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(resolved.Value.RunDirectory, file).Replace('\\', '/');
            Console.WriteLine(relative);
        }

        return 0;
    }
}
