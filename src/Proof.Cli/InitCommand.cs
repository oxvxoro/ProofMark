using System.CommandLine;
using Proof.Adapters.Distill;
using Proof.Core;

namespace Proof.Cli;

public static class InitCommand
{
    public static Command Create()
    {
        var forceOption = new Option<bool>("--force")
        {
            Description = "Overwrite an existing proof.yml."
        };

        var command = new Command("init", "Create proof.yml and distill.yml from bundled defaults when missing.")
        {
            forceOption
        };

        command.SetAction((parseResult, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var force = parseResult.GetValue(forceOption);
            return Task.FromResult(Execute(force));
        });

        return command;
    }

    internal static int Execute(bool force, string? workspaceRoot = null)
    {
        workspaceRoot = string.IsNullOrWhiteSpace(workspaceRoot)
            ? Directory.GetCurrentDirectory()
            : workspaceRoot;
        var targetPath = Path.Combine(workspaceRoot, "proof.yml");
        if (File.Exists(targetPath) && !force)
        {
            Console.WriteLine($"Config already exists: {targetPath}. Use --force to overwrite.");
            return 0;
        }

        try
        {
            var examplePath = ResolveExamplePath(workspaceRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(examplePath, targetPath, overwrite: force);
            Console.WriteLine($"Created {targetPath} from {Path.GetFileName(examplePath)}.");
            var solutionFileName = ProofInitWorkspace.FindPrimarySolutionFileName(workspaceRoot);
            ProofInitWorkspace.SubstituteSolutionPlaceholder(targetPath, solutionFileName);
            if (DistillConfigBootstrap.TryCreateDefaultConfig(workspaceRoot, out var distillPath, out var distillMessage))
            {
                Console.WriteLine(distillMessage);
            }
            else if (!string.IsNullOrWhiteSpace(distillPath) && File.Exists(distillPath))
            {
                Console.WriteLine($"Distill config already exists: {distillPath}.");
            }

            var previous = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(workspaceRoot);
                return ConfigValidateCommand.Validate(bootstrap: true);
            }
            finally
            {
                Directory.SetCurrentDirectory(previous);
            }
        }
        catch (ProofConfigException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    internal static string ResolveExamplePath(string workspaceRoot)
    {
        var local = Path.Combine(workspaceRoot, "proof.yml.example");
        if (File.Exists(local))
        {
            return local;
        }

        var bundled = Path.Combine(AppContext.BaseDirectory, "proof.yml.example");
        if (File.Exists(bundled))
        {
            return bundled;
        }

        throw new ProofConfigException(
            "proof.yml.example not found in the workspace or next to the proof tool. "
            + "Copy proof.yml.example from the Proof repository.");
    }
}
