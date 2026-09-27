using Distill.Core.Diagnostics;
using Distill.Core.Evidence;
using Distill.Core.Runs;
using Distill.Runner;
using Distill.Testing.Abstractions;

namespace Distill.Testing.VSTest;

public sealed class VstestTrxResultSource : ITestResultSource
{
    private readonly ProcessRunner _processRunner = new();
    private readonly TrxParser _parser = new();

    public string Id => "vstest-trx";

    public bool CanHandle(TestPlatformKind platform) => platform == TestPlatformKind.VSTest;

    public async Task<TestRunEvidence> ExecuteAndCollectAsync(
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

        var arguments = VstestCommandBuilder.BuildTrxArguments(check, trxPath);
        await _processRunner.RunAsync(
            new ProcessSpec(
                FileName: "dotnet",
                Arguments: arguments,
                WorkingDirectory: context.WorkspaceRoot,
                Timeout: check.Timeout ?? TimeSpan.FromMinutes(30),
                StdoutPath: Path.Combine(checkDirectory, "stdout.log"),
                StderrPath: Path.Combine(checkDirectory, "stderr.log")),
            cancellationToken).ConfigureAwait(false);

        if (!File.Exists(trxPath))
        {
            throw new FileNotFoundException("TRX fallback file was not created.", trxPath);
        }

        var evidence = _parser.Parse(trxPath, Id, check.Target);
        var diagnostics = VstestDiagnosticMapper.MapFailedCases(
            evidence.Cases,
            DiagnosticProvenance.Trx,
            confidence: 0.95);

        await TestDiagnosticsWriter.WriteAsync(
            checkDirectory,
            evidence,
            diagnostics,
            Id,
            cancellationToken).ConfigureAwait(false);

        return evidence;
    }

    internal async Task<TestRunEvidence> ParseExistingAsync(
        TestCheckDefinition check,
        string checkDirectory,
        string trxPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(trxPath))
        {
            throw new FileNotFoundException("TRX fallback file was not created.", trxPath);
        }

        var evidence = _parser.Parse(trxPath, Id, check.Target);
        var diagnostics = VstestDiagnosticMapper.MapFailedCases(
            evidence.Cases,
            DiagnosticProvenance.Trx,
            confidence: 0.95);

        await TestDiagnosticsWriter.WriteAsync(
            checkDirectory,
            evidence,
            diagnostics,
            Id,
            cancellationToken).ConfigureAwait(false);

        return evidence;
    }
}
