using Distill.Cli.Services;
using Distill.Core.Abstractions;
using Distill.Core.Config;
using System.CommandLine;

namespace Distill.Cli.Commands;

public static class VerifyCommand
{
    public static Command Create()
    {
        var profileOption = new Option<string>("--profile")
        {
            Description = "Verification profile from distill.yml.",
            DefaultValueFactory = _ => "quick"
        };

        var outputOption = new Option<string>("--output")
        {
            Description = "Output format: compact or json.",
            DefaultValueFactory = _ => "compact"
        };

        var command = new Command("verify", "Run configured verification checks and produce a Failure Pack.")
        {
            profileOption,
            outputOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var profile = parseResult.GetValue(profileOption)!;
            var output = parseResult.GetValue(outputOption)!;
            return await ExecuteAsync(profile, output, cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    internal static async Task<int> ExecuteAsync(
        string profile,
        string output,
        CancellationToken cancellationToken)
    {
        var workspaceRoot = Directory.GetCurrentDirectory();
        var configPath = WorkspaceDiscovery.ResolveConfigPath(workspaceRoot);

        DistillConfig config;
        try
        {
            config = DistillConfigLoader.Load(configPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Config error: {ex.Message}");
            Console.Error.WriteLine("Run `distill init` to create distill.yml.");
            return ExitCodeMapper.ConfigError;
        }

        if (!config.Profiles.ContainsKey(profile))
        {
            Console.Error.WriteLine($"Profile '{profile}' was not found in {configPath}.");
            return ExitCodeMapper.ConfigError;
        }

        var format = string.Equals(output, "json", StringComparison.OrdinalIgnoreCase)
            ? ReportOutputFormat.Json
            : ReportOutputFormat.Compact;

        try
        {
            var orchestrator = new VerifyOrchestrator();
            var result = await orchestrator.RunAsync(
                workspaceRoot,
                config,
                profile,
                new ReportOptions(format),
                cancellationToken).ConfigureAwait(false);

            if (format == ReportOutputFormat.Json && result.Pack.JsonText is not null)
            {
                Console.WriteLine(result.Pack.JsonText);
            }
            else
            {
                Console.WriteLine(result.Pack.CompactText);
            }

            return result.ExitCode;
        }
        catch (OperationCanceledException)
        {
            return ExitCodeMapper.Canceled;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Verification failed to run: {ex.Message}");
            return ExitCodeMapper.FromStatus(Distill.Core.Runs.VerificationStatus.InfraError);
        }
    }
}
