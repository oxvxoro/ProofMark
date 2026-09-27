using Distill.Core.Diagnostics;
using Distill.Core.Evidence;
using Distill.Core.Runs;
using Distill.Runner;
using Distill.Testing.Abstractions;

namespace Distill.Testing.VSTest;

public sealed class VstestLoggerResultSource : ITestResultSource
{
    private readonly ProcessRunner _processRunner = new();
    private readonly JsonlTestResultParser _parser = new();
    private readonly string? _extensionDirectoryOverride;

    public VstestLoggerResultSource(string? extensionDirectoryOverride = null)
    {
        _extensionDirectoryOverride = extensionDirectoryOverride;
    }

    public string Id => "vstest-logger";

    public bool CanHandle(TestPlatformKind platform) => platform == TestPlatformKind.VSTest;

    public async Task<TestRunEvidence> ExecuteAndCollectAsync(
        TestCheckDefinition check,
        DistillRunContext context,
        CancellationToken cancellationToken)
    {
        var checkDirectory = RunArtifactLayout.GetCheckDirectory(context.RunDirectory, check.Id);
        Directory.CreateDirectory(checkDirectory);

        var eventsPath = Path.Combine(checkDirectory, "tests.events.jsonl");
        var extensionDirectory = _extensionDirectoryOverride ?? TestLoggerPathResolver.ResolveExtensionDirectory();
        var arguments = VstestCommandBuilder.BuildLoggerArguments(check, extensionDirectory, eventsPath);

        var processResult = await _processRunner.RunAsync(
            new ProcessSpec(
                FileName: "dotnet",
                Arguments: arguments,
                WorkingDirectory: context.WorkspaceRoot,
                Timeout: check.Timeout ?? TimeSpan.FromMinutes(30),
                StdoutPath: Path.Combine(checkDirectory, "stdout.log"),
                StderrPath: Path.Combine(checkDirectory, "stderr.log")),
            cancellationToken).ConfigureAwait(false);

        var parseResult = _parser.Parse(eventsPath, Id, check.Target);
        if (parseResult.Evidence is not null && parseResult.IsComplete)
        {
            await TestDiagnosticsWriter.WriteAsync(
                checkDirectory,
                parseResult.Evidence,
                parseResult.Diagnostics,
                Id,
                cancellationToken).ConfigureAwait(false);

            return parseResult.Evidence;
        }

        throw new InvalidOperationException(parseResult.ErrorMessage ?? "VSTest logger collection failed.");
    }

    internal async Task<(TestRunEvidence? Evidence, string? Error, ProcessResult ProcessResult)> TryCollectAsync(
        TestCheckDefinition check,
        DistillRunContext context,
        CancellationToken cancellationToken)
    {
        var checkDirectory = RunArtifactLayout.GetCheckDirectory(context.RunDirectory, check.Id);
        Directory.CreateDirectory(checkDirectory);

        var eventsPath = Path.Combine(checkDirectory, "tests.events.jsonl");
        var trxPath = Path.Combine(checkDirectory, "fallback.trx");
        if (File.Exists(eventsPath))
        {
            File.Delete(eventsPath);
        }

        if (File.Exists(trxPath))
        {
            File.Delete(trxPath);
        }

        string extensionDirectory;
        try
        {
            extensionDirectory = _extensionDirectoryOverride ?? TestLoggerPathResolver.ResolveExtensionDirectory();
        }
        catch (Exception ex)
        {
            return (null, ex.Message, new ProcessResult(null, TimeSpan.Zero, ProcessStatus.FailedToStart, null, null, ex.Message));
        }

        var arguments = VstestCommandBuilder.BuildDualLoggerArguments(check, extensionDirectory, eventsPath, trxPath);
        var processResult = await _processRunner.RunAsync(
            new ProcessSpec(
                FileName: "dotnet",
                Arguments: arguments,
                WorkingDirectory: context.WorkspaceRoot,
                Timeout: check.Timeout ?? TimeSpan.FromMinutes(30),
                StdoutPath: Path.Combine(checkDirectory, "logger.stdout.log"),
                StderrPath: Path.Combine(checkDirectory, "logger.stderr.log")),
            cancellationToken).ConfigureAwait(false);

        if (!File.Exists(eventsPath))
        {
            return (null, $"JSONL file was not created. AdapterPath={extensionDirectory}", processResult);
        }

        var parseResult = _parser.Parse(eventsPath, Id, check.Target);
        if (parseResult.Evidence is not null && parseResult.IsComplete)
        {
            await TestDiagnosticsWriter.WriteAsync(
                checkDirectory,
                parseResult.Evidence,
                parseResult.Diagnostics,
                Id,
                cancellationToken).ConfigureAwait(false);

            return (parseResult.Evidence, null, processResult);
        }

        return (null, parseResult.ErrorMessage, processResult);
    }
}
