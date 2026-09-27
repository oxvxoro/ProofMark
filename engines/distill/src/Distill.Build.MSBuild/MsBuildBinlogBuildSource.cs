using System.Text.Json;
using Distill.Core.Abstractions;
using Distill.Core.Diagnostics;
using Distill.Core.Evidence;
using Distill.Core.Planning;
using Distill.Core.Runs;
using Distill.Runner;

namespace Distill.Build.MSBuild;

public sealed class MsBuildBinlogBuildSource : IBuildEvidenceSource
{
    private readonly ProcessRunner _processRunner = new();
    private readonly BinaryLogReader _binaryLogReader = new();

    public async Task<BuildEvidence> RunAsync(
        BuildCheckDefinition check,
        DistillRunContext context,
        CancellationToken cancellationToken)
    {
        var buildDirectory = RunArtifactLayout.GetCheckDirectory(context.RunDirectory, check.Id);
        Directory.CreateDirectory(buildDirectory);

        var binlogPath = Path.Combine(buildDirectory, "build.binlog");
        var stdoutPath = Path.Combine(buildDirectory, "stdout.log");
        var stderrPath = Path.Combine(buildDirectory, "stderr.log");

        var arguments = BuildCommandBuilder.BuildArguments(check, binlogPath);
        var processResult = await _processRunner.RunAsync(
            new ProcessSpec(
                FileName: "dotnet",
                Arguments: arguments,
                WorkingDirectory: context.WorkspaceRoot,
                Timeout: check.Timeout ?? TimeSpan.FromMinutes(30),
                StdoutPath: stdoutPath,
                StderrPath: stderrPath),
            cancellationToken).ConfigureAwait(false);

        if (processResult.Status is ProcessStatus.Canceled)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (processResult.Status is ProcessStatus.Canceled or ProcessStatus.FailedToStart or ProcessStatus.TimedOut)
        {
            return new BuildEvidence(
                Diagnostics: new[]
                {
                    DistillDiagnostic.Create(
                        id: "build-infra-1",
                        kind: DiagnosticKind.Infrastructure,
                        severity: DiagnosticSeverity.Error,
                        source: "distill",
                        code: processResult.Status.ToString().ToUpperInvariant(),
                        message: processResult.ErrorMessage ?? $"Build process ended with status {processResult.Status}.",
                        provenance: DiagnosticProvenance.RawFallback,
                        confidence: 1.0)
                },
                Succeeded: false,
                BinlogPath: File.Exists(binlogPath) ? binlogPath : null,
                Warnings: Array.Empty<DistillDiagnostic>());
        }

        if (!File.Exists(binlogPath))
        {
            return new BuildEvidence(
                Diagnostics: new[]
                {
                    DistillDiagnostic.Create(
                        id: "build-infra-2",
                        kind: DiagnosticKind.Infrastructure,
                        severity: DiagnosticSeverity.Error,
                        source: "distill",
                        code: "BINLOG_MISSING",
                        message: "Build completed but no binlog was produced.",
                        provenance: DiagnosticProvenance.RawFallback,
                        confidence: 1.0)
                },
                Succeeded: false,
                BinlogPath: null,
                Warnings: Array.Empty<DistillDiagnostic>());
        }

        var replay = _binaryLogReader.Read(binlogPath, cancellationToken);
        var succeeded = replay.BuildSucceeded && replay.Errors.Count == 0 && processResult.ExitCode == 0;

        if (!succeeded && replay.Errors.Count == 0 && processResult.ExitCode != 0)
        {
            replay = replay with
            {
                Errors = new[]
                {
                    DistillDiagnostic.Create(
                        id: "build-infra-3",
                        kind: DiagnosticKind.Infrastructure,
                        severity: DiagnosticSeverity.Error,
                        source: "distill",
                        code: "BUILD_FAILED_NO_DIAGNOSTICS",
                        message: $"Build exited with code {processResult.ExitCode} but no MSBuild errors were found in the binlog.",
                        provenance: DiagnosticProvenance.RawFallback,
                        confidence: 0.5)
                }
            };
            succeeded = false;
        }

        if (replay.FormatVersionMismatch && replay.Errors.Count == 0 && !succeeded)
        {
            replay = replay with
            {
                Errors = new[]
                {
                    DistillDiagnostic.Create(
                        id: "build-infra-4",
                        kind: DiagnosticKind.Infrastructure,
                        severity: DiagnosticSeverity.Error,
                        source: "distill",
                        code: "BINLOG_VERSION_MISMATCH",
                        message: "Binlog format version mismatch; evidence preservation is uncertain.",
                        provenance: DiagnosticProvenance.RawFallback,
                        confidence: 0.3)
                }
            };
        }

        var evidence = new BuildEvidence(
            Diagnostics: replay.Errors,
            Succeeded: succeeded,
            BinlogPath: binlogPath,
            Warnings: replay.Warnings,
            BuiltProjects: replay.BuiltProjects,
            BuiltAssemblies: replay.BuiltAssemblies);

        await WriteDiagnosticsAsync(buildDirectory, evidence, cancellationToken).ConfigureAwait(false);

        return evidence;
    }

    private static async Task WriteDiagnosticsAsync(
        string checkDirectory,
        BuildEvidence evidence,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            succeeded = evidence.Succeeded,
            binlogPath = evidence.BinlogPath,
            errors = evidence.Diagnostics,
            warnings = evidence.Warnings
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(
            Path.Combine(checkDirectory, "diagnostics.json"),
            json,
            cancellationToken).ConfigureAwait(false);
    }
}
