using Distill.Build.MSBuild;
using Distill.Core.Planning;
using Distill.Core.Runs;
using Distill.Reporting;
using System.CommandLine;

namespace Distill.Cli.Commands;

public static class BuildSpikeCommand
{
    public static Command Create()
    {
        var pathArgument = new Argument<string?>("path")
        {
            Description = "Path to a solution or project to build.",
            Arity = ArgumentArity.ZeroOrOne
        };

        var command = new Command("build-spike", "Run a structured MSBuild binlog build spike.")
        {
            pathArgument
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var path = parseResult.GetValue(pathArgument);
            return await ExecuteAsync(path, cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    internal static async Task<int> ExecuteAsync(string? path, CancellationToken cancellationToken)
    {
        var workspaceRoot = Directory.GetCurrentDirectory();
        var runId = RunIdGenerator.Create();
        var runDirectory = RunArtifactLayout.GetRunDirectory(workspaceRoot, runId);

        Directory.CreateDirectory(runDirectory);
        await RunManifestWriter.WriteAsync(
            runDirectory,
            new RunManifest(runId, DateTimeOffset.UtcNow, workspaceRoot),
            cancellationToken).ConfigureAwait(false);

        var context = new DistillRunContext
        {
            RunId = runId,
            WorkspaceRoot = workspaceRoot,
            RunDirectory = runDirectory,
            Profile = "build-spike"
        };

        var target = ResolveTarget(path, workspaceRoot);
        var check = new BuildCheckDefinition(
            Id: "build",
            Target: target,
            Arguments: Array.Empty<string>());

        var source = new MsBuildBinlogBuildSource();
        var evidence = await source.RunAsync(check, context, cancellationToken).ConfigureAwait(false);
        var output = BuildFailureFormatter.Format(evidence, context);
        var status = BuildFailureFormatter.ResolveStatus(evidence);

        Console.WriteLine(output);

        return status switch
        {
            VerificationStatus.Pass => 0,
            VerificationStatus.Fail => 1,
            VerificationStatus.Uncertain => 4,
            VerificationStatus.InfraError => 3,
            _ => 3
        };
    }

    private static string ResolveTarget(string? path, string workspaceRoot)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            return Path.GetFullPath(path);
        }

        var solution = Directory.EnumerateFiles(workspaceRoot, "*.sln").FirstOrDefault();
        if (solution is not null)
        {
            return solution;
        }

        var project = Directory.EnumerateFiles(workspaceRoot, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();
        if (project is not null)
        {
            return project;
        }

        throw new InvalidOperationException("No solution or project found. Pass a path argument.");
    }
}
