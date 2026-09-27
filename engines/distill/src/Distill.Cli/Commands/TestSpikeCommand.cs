using Distill.Core.Runs;
using Distill.Reporting;
using Distill.Testing.Abstractions;
using Distill.Testing.VSTest;
using System.CommandLine;

namespace Distill.Cli.Commands;

public static class TestSpikeCommand
{
    public static Command Create()
    {
        var pathArgument = new Argument<string?>("path")
        {
            Description = "Path to a solution or test project.",
            Arity = ArgumentArity.ZeroOrOne
        };

        var command = new Command("test-spike", "Run a structured VSTest logger spike with TRX fallback.")
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
            Profile = "test-spike"
        };

        var target = ResolveTarget(path, workspaceRoot);
        var check = new TestCheckDefinition(
            Id: "unit",
            Target: target,
            Arguments: Array.Empty<string>());

        var source = new VstestCompositeResultSource(TestLoggerPathResolver.ResolveExtensionDirectory());
        var evidence = await source.ExecuteAndCollectAsync(check, context, cancellationToken).ConfigureAwait(false);
        var diagnostics = VstestCompositeResultSource.ResolveDiagnostics(evidence, evidence.SourceId);
        var output = TestFailureFormatter.Format(evidence, diagnostics, context, evidence.SourceId);
        var status = TestFailureFormatter.ResolveStatus(evidence);

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

        var testProject = Directory
            .EnumerateFiles(workspaceRoot, "*.csproj", SearchOption.AllDirectories)
            .FirstOrDefault(project => project.Contains("Tests", StringComparison.OrdinalIgnoreCase)
                                       || project.Contains("Test.", StringComparison.OrdinalIgnoreCase));

        if (testProject is not null)
        {
            return testProject;
        }

        var solution = Directory.EnumerateFiles(workspaceRoot, "*.sln").FirstOrDefault();
        if (solution is not null)
        {
            return solution;
        }

        throw new InvalidOperationException("No test project or solution found. Pass a path argument.");
    }
}
