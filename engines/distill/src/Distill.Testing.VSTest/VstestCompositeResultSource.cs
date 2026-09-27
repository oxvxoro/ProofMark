using Distill.Core.Diagnostics;
using Distill.Core.Evidence;
using Distill.Core.Runs;
using Distill.Runner;
using Distill.Testing.Abstractions;

namespace Distill.Testing.VSTest;

public sealed record VstestExecutionResult(
    TestRunEvidence Evidence,
    ProcessResult ProcessResult,
    string SourceId);

public sealed class VstestCompositeResultSource : ITestResultSource
{
    private readonly VstestLoggerResultSource _loggerSource;
    private readonly VstestTrxResultSource _trxSource = new();

    public VstestCompositeResultSource(string? loggerExtensionDirectory = null)
    {
        _loggerSource = new VstestLoggerResultSource(loggerExtensionDirectory);
    }

    public string Id => "vstest-composite";

    public bool CanHandle(TestPlatformKind platform) => platform == TestPlatformKind.VSTest;

    public async Task<TestRunEvidence> ExecuteAndCollectAsync(
        TestCheckDefinition check,
        DistillRunContext context,
        CancellationToken cancellationToken)
    {
        var result = await ExecuteWithProcessAsync(check, context, cancellationToken).ConfigureAwait(false);
        return result.Evidence;
    }

    public async Task<VstestExecutionResult> ExecuteWithProcessAsync(
        TestCheckDefinition check,
        DistillRunContext context,
        CancellationToken cancellationToken)
    {
        var loggerAttempt = await _loggerSource.TryCollectAsync(check, context, cancellationToken).ConfigureAwait(false);
        if (loggerAttempt.Evidence is not null)
        {
            return new VstestExecutionResult(loggerAttempt.Evidence, loggerAttempt.ProcessResult, "vstest-logger");
        }

        var checkDirectory = RunArtifactLayout.GetCheckDirectory(context.RunDirectory, check.Id);
        var trxPath = Path.Combine(checkDirectory, "fallback.trx");
        if (File.Exists(trxPath))
        {
            try
            {
                var evidence = await _trxSource.ParseExistingAsync(check, checkDirectory, trxPath, cancellationToken)
                    .ConfigureAwait(false);
                return new VstestExecutionResult(evidence, loggerAttempt.ProcessResult, "vstest-trx");
            }
            catch (Exception ex) when (loggerAttempt.ProcessResult.Status is ProcessStatus.Completed)
            {
                throw new InvalidOperationException(
                    $"VSTest logger failed ({loggerAttempt.Error}) and TRX fallback parse failed ({ex.Message}).",
                    ex);
            }
        }

        if (loggerAttempt.ProcessResult.Status is ProcessStatus.Canceled)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (loggerAttempt.ProcessResult.Status is ProcessStatus.TimedOut
            or ProcessStatus.Canceled
            or ProcessStatus.FailedToStart)
        {
            throw new InvalidOperationException(
                loggerAttempt.ProcessResult.ErrorMessage
                ?? $"VSTest process ended with status {loggerAttempt.ProcessResult.Status}.");
        }

        throw new InvalidOperationException(loggerAttempt.Error ?? "VSTest logger collection failed.");
    }

    public static VerificationStatus ResolveStatus(TestRunEvidence evidence, ProcessResult? processResult = null)
    {
        if (processResult?.Status is ProcessStatus.TimedOut
            or ProcessStatus.Canceled
            or ProcessStatus.FailedToStart)
        {
            return VerificationStatus.InfraError;
        }

        if (evidence.Failed > 0)
        {
            return VerificationStatus.Fail;
        }

        if (evidence.Cases.Count == 0)
        {
            return VerificationStatus.Uncertain;
        }

        if (processResult?.Status == ProcessStatus.Completed
            && processResult.ExitCode is int exitCode
            && exitCode != 0)
        {
            return VerificationStatus.InfraError;
        }

        return VerificationStatus.Pass;
    }

    public static IReadOnlyList<DistillDiagnostic> ResolveDiagnostics(TestRunEvidence evidence, string sourceId)
    {
        var provenance = string.Equals(sourceId, "vstest-trx", StringComparison.Ordinal)
            ? DiagnosticProvenance.Trx
            : DiagnosticProvenance.VSTestLoggerEvent;
        var confidence = provenance == DiagnosticProvenance.Trx ? 0.95 : 1.0;
        return VstestDiagnosticMapper.MapFailedCases(evidence.Cases, provenance, confidence);
    }
}