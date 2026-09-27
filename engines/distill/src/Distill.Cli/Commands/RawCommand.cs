using System.CommandLine;

namespace Distill.Cli.Commands;

public static class RawCommand
{
    public static Command Create()
    {
        var checkArgument = new Argument<string>("check")
        {
            Description = "Check id (for example build or unit)."
        };

        var tailOption = new Option<int>("--tail")
        {
            Description = "Number of trailing lines to print.",
            DefaultValueFactory = _ => 200
        };

        var grepOption = new Option<string?>("--grep")
        {
            Description = "Only print lines containing this substring."
        };

        var runOption = new Option<string?>("--run")
        {
            Description = "Run id to inspect. Defaults to latest run."
        };

        var command = new Command("raw", "Show raw stdout/stderr logs for a check.")
        {
            checkArgument,
            tailOption,
            grepOption,
            runOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var checkId = parseResult.GetValue(checkArgument)!;
            var tail = parseResult.GetValue(tailOption);
            var grep = parseResult.GetValue(grepOption);
            var runId = parseResult.GetValue(runOption);
            return await ExecuteAsync(checkId, tail, grep, runId, cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    internal static async Task<int> ExecuteAsync(
        string checkId,
        int tail,
        string? grep,
        string? runId,
        CancellationToken cancellationToken)
    {
        var workspaceRoot = Directory.GetCurrentDirectory();
        var resolved = await RunContextResolver.ResolveLatestAsync(workspaceRoot, runId, cancellationToken)
            .ConfigureAwait(false);

        if (resolved is null)
        {
            Console.Error.WriteLine("No verification run found.");
            return ExitCodeMapper.ConfigError;
        }

        var checkDirectory = Distill.Core.Runs.RunArtifactLayout.GetCheckDirectory(resolved.Value.RunDirectory, checkId);
        if (!Directory.Exists(checkDirectory))
        {
            Console.Error.WriteLine($"Check directory not found: {checkDirectory}");
            return ExitCodeMapper.ConfigError;
        }

        var candidates = new[]
        {
            Path.Combine(checkDirectory, "stdout.log"),
            Path.Combine(checkDirectory, "stderr.log"),
            Path.Combine(checkDirectory, "logger.stdout.log"),
            Path.Combine(checkDirectory, "logger.stderr.log")
        };

        var found = false;
        foreach (var path in candidates.Where(File.Exists))
        {
            found = true;
            Console.WriteLine($"--- {Path.GetFileName(path)} ---");
            var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
            var slice = lines.Length <= tail ? lines : lines[^tail..];
            foreach (var line in slice)
            {
                if (grep is not null && !line.Contains(grep, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Console.WriteLine(line);
            }
        }

        if (!found)
        {
            Console.Error.WriteLine($"No raw logs found under {checkDirectory}");
            return ExitCodeMapper.FromStatus(Distill.Core.Runs.VerificationStatus.InfraError);
        }

        return 0;
    }
}
