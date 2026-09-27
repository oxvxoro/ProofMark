using Distill.Build.MSBuild;
using Distill.Analysis.Sarif;
using Distill.Core.Analysis;
using Distill.Core.Diagnostics;
using Distill.Core.Evidence;
using Distill.Core.Planning;
using Distill.Core.Runs;
using Distill.Runner;
using Distill.Testing.Abstractions;
using Distill.Testing.MTP;
using Distill.Testing.VSTest;
using Distill.Reporting;

namespace Distill.Execution;

public sealed class DistillCheckExecutor : ICheckExecutor
{
    private readonly MsBuildBinlogBuildSource _buildSource = new();
    private readonly VstestCompositeResultSource _vstestSource;
    private readonly MtpReportResultSource _mtpSource = new();
    private readonly SarifAnalysisSource _sarifSource = new();
    private readonly ProcessRunner _processRunner = new();
    private readonly ITestPlatformDetector _platformDetector = new MtpPlatformDetector();
    private readonly string _configuredPlatform;
    private readonly IReadOnlyDictionary<string, ICheckKindHandler> _handlers;

    public DistillCheckExecutor(
        string? loggerExtensionDirectory = null,
        string configuredPlatform = "auto")
    {
        _vstestSource = new VstestCompositeResultSource(loggerExtensionDirectory);
        _configuredPlatform = configuredPlatform;
        _handlers = BuildHandlerMap();
    }

    private IReadOnlyDictionary<string, ICheckKindHandler> BuildHandlerMap() =>
        new Dictionary<string, ICheckKindHandler>(StringComparer.OrdinalIgnoreCase)
        {
            ["build"] = new BuildCheckKindHandler(this),
            ["test"] = new TestCheckKindHandler(this),
            ["analysis"] = new AnalysisCheckKindHandler(this),
            ["apicompatibility"] = new ApiCompatCheckKindHandler(this),
            ["api_compatibility"] = new ApiCompatCheckKindHandler(this),
            ["api-compatibility"] = new ApiCompatCheckKindHandler(this),
        };

    public async Task<CheckRunResult> ExecuteAsync(
        PlannedCheck check,
        DistillRunContext context,
        CancellationToken cancellationToken)
    {
        var kind = check.Definition.Kind.ToLowerInvariant();
        if (_handlers.TryGetValue(kind, out var handler))
            return await handler.ExecuteAsync(check, context, cancellationToken).ConfigureAwait(false);

        var started = DateTimeOffset.UtcNow;
        var timeout = TimeSpan.FromSeconds(check.Definition.Timeout);
        return await ExecuteGenericAsync(check, context, timeout, started, kind, cancellationToken).ConfigureAwait(false);
    }

    private interface ICheckKindHandler
    {
        Task<CheckRunResult> ExecuteAsync(PlannedCheck check, DistillRunContext context, CancellationToken cancellationToken);
    }

    private sealed class BuildCheckKindHandler(DistillCheckExecutor owner) : ICheckKindHandler
    {
        public Task<CheckRunResult> ExecuteAsync(PlannedCheck check, DistillRunContext context, CancellationToken cancellationToken)
        {
            var started = DateTimeOffset.UtcNow;
            var timeout = TimeSpan.FromSeconds(check.Definition.Timeout);
            return owner.ExecuteBuildAsync(check, context, timeout, started, cancellationToken);
        }
    }

    private sealed class TestCheckKindHandler(DistillCheckExecutor owner) : ICheckKindHandler
    {
        public Task<CheckRunResult> ExecuteAsync(PlannedCheck check, DistillRunContext context, CancellationToken cancellationToken)
        {
            var started = DateTimeOffset.UtcNow;
            var timeout = TimeSpan.FromSeconds(check.Definition.Timeout);
            return owner.ExecuteTestAsync(check, context, timeout, started, cancellationToken);
        }
    }

    private sealed class AnalysisCheckKindHandler(DistillCheckExecutor owner) : ICheckKindHandler
    {
        public Task<CheckRunResult> ExecuteAsync(PlannedCheck check, DistillRunContext context, CancellationToken cancellationToken)
        {
            var started = DateTimeOffset.UtcNow;
            return owner.ExecuteAnalysisAsync(check, context, started, cancellationToken);
        }
    }

    private sealed class ApiCompatCheckKindHandler(DistillCheckExecutor owner) : ICheckKindHandler
    {
        public Task<CheckRunResult> ExecuteAsync(PlannedCheck check, DistillRunContext context, CancellationToken cancellationToken)
        {
            var started = DateTimeOffset.UtcNow;
            var timeout = TimeSpan.FromSeconds(check.Definition.Timeout);
            return owner.ExecuteApiCompatAsync(check, context, timeout, started, cancellationToken);
        }
    }

    private async Task<CheckRunResult> ExecuteApiCompatAsync(
        PlannedCheck check,
        DistillRunContext context,
        TimeSpan timeout,
        DateTimeOffset started,
        CancellationToken cancellationToken)
    {
        var checkDirectory = RunArtifactLayout.GetCheckDirectory(context.RunDirectory, check.Id);
        Directory.CreateDirectory(checkDirectory);
        var reportPath = Path.Combine(checkDirectory, "apicompat.json");
        if (!string.IsNullOrWhiteSpace(check.Definition.Command))
        {
            await ExecuteGenericAsync(check, context, timeout, started, "apicompatibility", cancellationToken)
                .ConfigureAwait(false);
        }

        await ApiCompatReportGenerator.WriteReportAsync(context, reportPath, cancellationToken)
            .ConfigureAwait(false);
        if (!ApiCompatReport.TryRead(reportPath, out var projects, out _))
        {
            return new CheckRunResult(
                check.Id,
                check.Definition.Kind,
                VerificationStatus.Uncertain,
                1,
                [],
                "apicompat-json",
                reportPath,
                DateTimeOffset.UtcNow - started);
        }

        var diagnostics = ApiCompatReport.ToDiagnostics(check.Id, projects);
        var status = ApiCompatReport.ResolveStatus(projects);
        return new CheckRunResult(
            check.Id,
            check.Definition.Kind,
            status,
            status == VerificationStatus.Fail ? 1 : 0,
            diagnostics,
            "apicompat-json",
            reportPath,
            DateTimeOffset.UtcNow - started);
    }

    private async Task<CheckRunResult> ExecuteAnalysisAsync(
        PlannedCheck check,
        DistillRunContext context,
        DateTimeOffset started,
        CancellationToken cancellationToken)
    {
        var checkDirectory = RunArtifactLayout.GetCheckDirectory(context.RunDirectory, check.Id);
        var result = await _sarifSource.RunAsync(
            check.Definition,
            context,
            checkDirectory,
            cancellationToken).ConfigureAwait(false);

        return new CheckRunResult(
            check.Id,
            check.Definition.Kind,
            result.Status,
            result.ExitCode,
            result.Diagnostics,
            "sarif",
            result.ArtifactPath ?? checkDirectory,
            DateTimeOffset.UtcNow - started);
    }

    private async Task<CheckRunResult> ExecuteBuildAsync(
        PlannedCheck check,
        DistillRunContext context,
        TimeSpan timeout,
        DateTimeOffset started,
        CancellationToken cancellationToken)
    {
        var parsed = DotnetCommandParser.Parse(check.Definition.Command);
        if (!string.Equals(parsed.Verb, "build", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Check '{check.Id}' is kind build but command verb is '{parsed.Verb}'.");
        }

        var buildCheck = new BuildCheckDefinition(
            Id: check.Id,
            Target: parsed.Target ?? string.Empty,
            Arguments: parsed.Arguments,
            Timeout: timeout);

        var evidence = await _buildSource.RunAsync(buildCheck, context, cancellationToken).ConfigureAwait(false);
        var status = ResolveBuildStatus(evidence);
        var exitCode = evidence.Succeeded ? 0 : 1;
        var checkDirectory = RunArtifactLayout.GetCheckDirectory(context.RunDirectory, check.Id);

        return new CheckRunResult(
            check.Id,
            check.Definition.Kind,
            status,
            exitCode,
            evidence.Diagnostics,
            "msbuild-binlog",
            evidence.BinlogPath ?? Path.Combine(checkDirectory, "build.binlog"),
            DateTimeOffset.UtcNow - started);
    }

    private async Task<CheckRunResult> ExecuteTestAsync(
        PlannedCheck check,
        DistillRunContext context,
        TimeSpan timeout,
        DateTimeOffset started,
        CancellationToken cancellationToken)
    {
        var parsed = DotnetCommandParser.Parse(check.Definition.Command);
        if (!string.Equals(parsed.Verb, "test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Check '{check.Id}' is kind test but command verb is '{parsed.Verb}'.");
        }

        var sourceHint = string.Equals(check.Definition.Source, "auto", StringComparison.OrdinalIgnoreCase)
            ? _configuredPlatform
            : check.Definition.Source;
        var platform = await _platformDetector.DetectAsync(
                context.WorkspaceRoot,
                parsed.Target,
                cancellationToken,
                sourceHint)
            .ConfigureAwait(false);

        var testCheck = new TestCheckDefinition(
            Id: check.Id,
            Target: parsed.Target ?? string.Empty,
            Arguments: parsed.Arguments,
            SourceHint: check.Definition.Source,
            Timeout: timeout);

        if (platform == TestPlatformKind.Mtp && !IsExplicitVstestSource(check.Definition.Source))
        {
            return await ExecuteMtpTestAsync(check, testCheck, context, started, cancellationToken)
                .ConfigureAwait(false);
        }

        TestRunEvidence evidence;
        string sourceId;
        ProcessResult processResult;
        try
        {
            var execution = await _vstestSource.ExecuteWithProcessAsync(testCheck, context, cancellationToken)
                .ConfigureAwait(false);
            evidence = execution.Evidence;
            processResult = execution.ProcessResult;
            sourceId = execution.SourceId;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new CheckRunResult(
                check.Id,
                check.Definition.Kind,
                VerificationStatus.InfraError,
                null,
                new[]
                {
                    DistillDiagnostic.Create(
                        id: $"{check.Id}-test-infra",
                        kind: DiagnosticKind.Infrastructure,
                        severity: DiagnosticSeverity.Error,
                        source: "distill",
                        code: "TEST_COLLECTION_FAILED",
                        message: ex.Message,
                        provenance: DiagnosticProvenance.RawFallback,
                        confidence: 1.0)
                },
                "vstest-composite",
                null,
                DateTimeOffset.UtcNow - started);
        }

        var diagnostics = VstestCompositeResultSource.ResolveDiagnostics(evidence, sourceId);
        var status = VstestCompositeResultSource.ResolveStatus(evidence, processResult);
        var exitCode = processResult.ExitCode ?? (evidence.Failed > 0 ? 1 : 0);
        var checkDirectory = RunArtifactLayout.GetCheckDirectory(context.RunDirectory, check.Id);
        var artifactPointer = Path.Combine(checkDirectory, "tests.events.jsonl");
        if (!File.Exists(artifactPointer))
        {
            artifactPointer = Path.Combine(checkDirectory, "fallback.trx");
        }

        return new CheckRunResult(
            check.Id,
            check.Definition.Kind,
            status,
            exitCode,
            diagnostics,
            sourceId,
            File.Exists(artifactPointer) ? artifactPointer : checkDirectory,
            DateTimeOffset.UtcNow - started,
            evidence.Cases);
    }

    private async Task<CheckRunResult> ExecuteMtpTestAsync(
        PlannedCheck check,
        TestCheckDefinition testCheck,
        DistillRunContext context,
        DateTimeOffset started,
        CancellationToken cancellationToken)
    {
        try
        {
            var execution = await _mtpSource.ExecuteWithProcessAsync(testCheck, context, cancellationToken)
                .ConfigureAwait(false);
            var diagnostics = MtpReportResultSource.ResolveDiagnostics(execution.Evidence);
            var status = MtpReportResultSource.ResolveStatus(execution.Evidence, execution.ProcessResult);
            var exitCode = execution.ProcessResult.ExitCode ?? (execution.Evidence.Failed > 0 ? 1 : 0);
            var checkDirectory = RunArtifactLayout.GetCheckDirectory(context.RunDirectory, check.Id);
            var artifactPointer = Path.Combine(checkDirectory, "fallback.trx");

            return new CheckRunResult(
                check.Id,
                check.Definition.Kind,
                status,
                exitCode,
                diagnostics,
                execution.SourceId,
                File.Exists(artifactPointer) ? artifactPointer : checkDirectory,
                DateTimeOffset.UtcNow - started,
                execution.Evidence.Cases);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new CheckRunResult(
                check.Id,
                check.Definition.Kind,
                VerificationStatus.InfraError,
                null,
                new[]
                {
                    DistillDiagnostic.Create(
                        id: $"{check.Id}-test-infra",
                        kind: DiagnosticKind.Infrastructure,
                        severity: DiagnosticSeverity.Error,
                        source: "distill",
                        code: "TEST_COLLECTION_FAILED",
                        message: ex.Message,
                        provenance: DiagnosticProvenance.RawFallback,
                        confidence: 1.0)
                },
                _mtpSource.Id,
                null,
                DateTimeOffset.UtcNow - started);
        }
    }

    private async Task<CheckRunResult> ExecuteGenericAsync(
        PlannedCheck check,
        DistillRunContext context,
        TimeSpan timeout,
        DateTimeOffset started,
        string kind,
        CancellationToken cancellationToken)
    {
        var (fileName, arguments) = ResolveCommand(check.Definition.Command, kind);
        var checkDirectory = RunArtifactLayout.GetCheckDirectory(context.RunDirectory, check.Id);
        Directory.CreateDirectory(checkDirectory);

        var processResult = await _processRunner.RunAsync(
            new ProcessSpec(
                FileName: fileName,
                Arguments: arguments,
                WorkingDirectory: context.WorkspaceRoot,
                Timeout: timeout,
                StdoutPath: Path.Combine(checkDirectory, "stdout.log"),
                StderrPath: Path.Combine(checkDirectory, "stderr.log")),
            cancellationToken).ConfigureAwait(false);

        if (processResult.Status is ProcessStatus.Canceled)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        var diagnostics = new List<DistillDiagnostic>();
        var status = VerificationStatus.Pass;
        if (processResult.Status is ProcessStatus.Canceled)
        {
            status = VerificationStatus.InfraError;
            diagnostics.Add(CreateInfraDiagnostic(check.Id, "CANCELED", "Check was canceled."));
        }
        else if (processResult.Status is ProcessStatus.TimedOut or ProcessStatus.FailedToStart)
        {
            status = VerificationStatus.InfraError;
            diagnostics.Add(CreateInfraDiagnostic(
                check.Id,
                processResult.Status.ToString().ToUpperInvariant(),
                processResult.ErrorMessage ?? $"Process ended with status {processResult.Status}."));
        }
        else if (processResult.ExitCode != 0)
        {
            status = VerificationStatus.Fail;
            if (string.Equals(kind, "format", StringComparison.OrdinalIgnoreCase))
            {
                var formatLines = ReadOutputLines(checkDirectory);
                var parsedDiagnostics = FormatDiagnosticParser.Parse(formatLines);
                if (parsedDiagnostics.Count > 0)
                {
                    diagnostics.AddRange(parsedDiagnostics);
                }
                else
                {
                    status = VerificationStatus.Uncertain;
                    diagnostics.Add(DistillDiagnostic.Create(
                        id: $"{check.Id}-format-uncertain",
                        kind: DiagnosticKind.Format,
                        severity: DiagnosticSeverity.Error,
                        source: "distill",
                        code: "FORMAT_OUTPUT_UNPARSED",
                        message: $"Check '{check.Id}' failed but no format locations were parsed.",
                        provenance: DiagnosticProvenance.RawFallback,
                        confidence: 0.4));
                }
            }
            else
            {
                diagnostics.Add(DistillDiagnostic.Create(
                    id: $"{check.Id}-generic-fail",
                    kind: MapKind(kind),
                    severity: DiagnosticSeverity.Error,
                    source: "distill",
                    code: "CHECK_FAILED",
                    message: $"Check '{check.Id}' exited with code {processResult.ExitCode}.",
                    provenance: DiagnosticProvenance.GenericTextParser,
                    confidence: 0.7));
            }
        }

        IReadOnlyList<TestCaseEvidence>? executedCases = null;
        var artifactPointer = checkDirectory;
        if (string.Equals(kind, "process", StringComparison.OrdinalIgnoreCase)
            && string.Equals(check.Definition.Source, "junit", StringComparison.OrdinalIgnoreCase)
            && status is VerificationStatus.Pass or VerificationStatus.Fail)
        {
            var reportPath = Path.GetFullPath(Path.Combine(context.WorkspaceRoot, check.Definition.Artifact ?? string.Empty));
            var readError = ReadJUnitReport(reportPath, check.Definition.Project, started, out executedCases);
            if (readError is null)
            {
                artifactPointer = reportPath;
            }
            else
            {
                if (status == VerificationStatus.Pass)
                {
                    status = VerificationStatus.Uncertain;
                }

                diagnostics.Add(DistillDiagnostic.Create(
                    id: $"{check.Id}-junit",
                    kind: DiagnosticKind.Infrastructure,
                    severity: DiagnosticSeverity.Error,
                    source: "distill",
                    code: readError,
                    message: $"Check '{check.Id}' did not produce a readable JUnit report at '{check.Definition.Artifact}'.",
                    provenance: DiagnosticProvenance.RawFallback,
                    confidence: 1.0));
            }
        }

        return new CheckRunResult(
            check.Id,
            check.Definition.Kind,
            status,
            processResult.ExitCode,
            diagnostics,
            check.Definition.Source,
            artifactPointer,
            DateTimeOffset.UtcNow - started,
            executedCases);
    }

    // 이 실행 전에 있던 보고서는 이전 실행의 결과일 수 있으므로 읽지 않는다.
    // 파일 시스템 시각 해상도 때문에 2초 여유를 둔다.
    internal static string? ReadJUnitReport(
        string reportPath,
        string? project,
        DateTimeOffset started,
        out IReadOnlyList<TestCaseEvidence>? cases)
    {
        cases = null;
        if (!File.Exists(reportPath))
        {
            return "JUNIT_REPORT_MISSING";
        }

        if (File.GetLastWriteTimeUtc(reportPath) < started.UtcDateTime.AddSeconds(-2))
        {
            return "JUNIT_REPORT_STALE";
        }

        try
        {
            cases = JUnitXmlParser.Parse(reportPath, project);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return "JUNIT_REPORT_UNREADABLE";
        }
    }

    // kind: process는 첫 토큰을 실행 파일로 그대로 실행하고 종료 코드만 남긴다.
    // 출력에서 테스트 케이스나 커버리지를 읽지 않는다. 나머지 종류는 dotnet 명령이다.
    internal static (string FileName, IReadOnlyList<string> Arguments) ResolveCommand(string command, string kind)
    {
        if (string.Equals(kind, "process", StringComparison.OrdinalIgnoreCase))
        {
            var tokens = DotnetCommandParser.Tokenize(command);
            if (tokens.Count == 0)
            {
                throw new ArgumentException("Command must not be empty.", nameof(command));
            }

            return (tokens[0], tokens.Skip(1).ToArray());
        }

        return ("dotnet", DotnetCommandParser.ToArgumentList(DotnetCommandParser.Parse(command)));
    }

    private static IReadOnlyList<string> ReadOutputLines(string checkDirectory)
    {
        var paths = new[]
        {
            Path.Combine(checkDirectory, "stdout.log"),
            Path.Combine(checkDirectory, "stderr.log")
        };

        return paths
            .Where(File.Exists)
            .SelectMany(File.ReadLines)
            .ToList();
    }

    private static bool IsExplicitVstestSource(string source)
        => string.Equals(source, "vstest-logger", StringComparison.OrdinalIgnoreCase)
           || string.Equals(source, "trx", StringComparison.OrdinalIgnoreCase)
           || string.Equals(source, "vstest", StringComparison.OrdinalIgnoreCase);

    private static VerificationStatus ResolveBuildStatus(BuildEvidence evidence)
    {
        if (evidence.Succeeded)
        {
            return VerificationStatus.Pass;
        }

        if (evidence.Diagnostics.Any(diagnostic =>
                diagnostic.Kind == DiagnosticKind.Infrastructure && diagnostic.Confidence < 0.5))
        {
            return VerificationStatus.Uncertain;
        }

        if (evidence.Diagnostics.Any(diagnostic => diagnostic.Kind == DiagnosticKind.Infrastructure))
        {
            return VerificationStatus.InfraError;
        }

        return VerificationStatus.Fail;
    }

    private static DistillDiagnostic CreateInfraDiagnostic(string checkId, string code, string message)
        => DistillDiagnostic.Create(
            id: $"{checkId}-infra",
            kind: DiagnosticKind.Infrastructure,
            severity: DiagnosticSeverity.Error,
            source: "distill",
            code: code,
            message: message,
            provenance: DiagnosticProvenance.RawFallback,
            confidence: 1.0);

    private static DiagnosticKind MapKind(string kind)
        => kind.ToLowerInvariant() switch
        {
            "format" => DiagnosticKind.Format,
            "analysis" => DiagnosticKind.Analysis,
            _ => DiagnosticKind.Infrastructure
        };
}
