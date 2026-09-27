using Distill.Core.Diagnostics;
using Distill.Core.Evidence;
using Distill.Core.Runs;
using Distill.Runner;
using Distill.Testing.Abstractions;
using Distill.Testing.VSTest;

namespace Distill.Testing.MTP;

public sealed record MtpExecutionResult(
    TestRunEvidence Evidence,
    ProcessResult ProcessResult,
    string SourceId);

public sealed class MtpReportResultSource : ITestResultSource
{
    private readonly ProcessRunner _processRunner = new();
    private readonly TrxParser _parser = new();

    public string Id => "mtp-report";

    public bool CanHandle(TestPlatformKind platform) => platform == TestPlatformKind.Mtp;

    public async Task<TestRunEvidence> ExecuteAndCollectAsync(
        TestCheckDefinition check,
        DistillRunContext context,
        CancellationToken cancellationToken)
    {
        var result = await ExecuteWithProcessAsync(check, context, cancellationToken).ConfigureAwait(false);
        return result.Evidence;
    }

    public async Task<MtpExecutionResult> ExecuteWithProcessAsync(
        TestCheckDefinition check,
        DistillRunContext context,
        CancellationToken cancellationToken)
    {
        var checkDirectory = RunArtifactLayout.GetCheckDirectory(context.RunDirectory, check.Id);
        Directory.CreateDirectory(checkDirectory);

        var trxPath = Path.Combine(checkDirectory, "fallback.trx");
        if (File.Exists(trxPath))
        {
            File.Delete(trxPath);
        }

        var arguments = MtpCommandBuilder.BuildReportArguments(check, checkDirectory);
        var processResult = await _processRunner.RunAsync(
            new ProcessSpec(
                FileName: "dotnet",
                Arguments: arguments,
                WorkingDirectory: context.WorkspaceRoot,
                Timeout: check.Timeout ?? TimeSpan.FromMinutes(30),
                StdoutPath: Path.Combine(checkDirectory, "stdout.log"),
                StderrPath: Path.Combine(checkDirectory, "stderr.log")),
            cancellationToken).ConfigureAwait(false);

        if (processResult.Status is ProcessStatus.Canceled)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (processResult.Status is ProcessStatus.TimedOut
            or ProcessStatus.Canceled
            or ProcessStatus.FailedToStart)
        {
            throw new InvalidOperationException(
                processResult.ErrorMessage
                ?? $"MTP test process ended with status {processResult.Status}.");
        }

        if (!File.Exists(trxPath))
        {
            throw new InvalidOperationException(
                "MTP TRX report was not created. Ensure the test project references Microsoft.Testing.Extensions.TrxReport and the workspace uses the Microsoft.Testing.Platform dotnet test runner.");
        }

        var evidence = _parser.Parse(trxPath, Id);
        var diagnostics = ResolveDiagnostics(evidence);
        await MtpTestDiagnosticsWriter.WriteAsync(
            checkDirectory,
            evidence,
            diagnostics,
            Id,
            cancellationToken).ConfigureAwait(false);

        return new MtpExecutionResult(evidence, processResult, Id);
    }

    public static VerificationStatus ResolveStatus(TestRunEvidence evidence, ProcessResult? processResult = null)
        => VstestCompositeResultSource.ResolveStatus(evidence, processResult);

    public static IReadOnlyList<DistillDiagnostic> ResolveDiagnostics(TestRunEvidence evidence)
        => VstestDiagnosticMapper.MapFailedCases(
            evidence.Cases,
            DiagnosticProvenance.MtpStructuredReport,
            confidence: 0.95);
}
