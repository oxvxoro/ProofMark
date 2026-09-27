using Distill.Core.Config;
using System.CommandLine;

namespace Distill.Cli.Commands;

public static class InitCommand
{
    public static Command Create()
    {
        var forceOption = new Option<bool>("--force")
        {
            Description = "Overwrite an existing distill.yml."
        };

        var command = new Command("init", "Discover workspace layout and create distill.yml.")
        {
            forceOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var force = parseResult.GetValue(forceOption);
            return await ExecuteAsync(force, cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    internal static Task<int> ExecuteAsync(bool force, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var workspaceRoot = Directory.GetCurrentDirectory();
        var configPath = WorkspaceDiscovery.ResolveConfigPath(workspaceRoot);

        if (File.Exists(configPath) && !force)
        {
            Console.Error.WriteLine($"Config already exists: {configPath}. Use --force to overwrite.");
            return Task.FromResult(ExitCodeMapper.ConfigError);
        }

        var config = WorkspaceDiscovery.CreateDefaultConfig(workspaceRoot);
        var yaml = WorkspaceDiscovery.SerializeConfig(config);
        File.WriteAllText(configPath, yaml);

        Console.WriteLine($"Created {configPath}");
        Console.WriteLine("Profiles: quick, full");
        return Task.FromResult(0);
    }
}
