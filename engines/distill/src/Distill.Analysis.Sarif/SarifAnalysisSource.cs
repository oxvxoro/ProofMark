using System.Text.Json;
using Distill.Core.Config;
using Distill.Core.Diagnostics;
using Distill.Core.Planning;
using Distill.Core.Runs;
using Distill.Runner;

namespace Distill.Analysis.Sarif;

public sealed record SarifAnalysisResult(
    IReadOnlyList<DistillDiagnostic> Diagnostics,
    VerificationStatus Status,
    int? ExitCode,
    string? ArtifactPath,
    ProcessStatus ProcessStatus);

internal delegate Task<ProcessResult> ProcessRunAsync(ProcessSpec spec, CancellationToken cancellationToken);

public sealed class SarifAnalysisSource
{
    private readonly ProcessRunAsync _runProcess;

    public SarifAnalysisSource()
        : this(CreateDefaultRunner())
    {
    }

    internal SarifAnalysisSource(ProcessRunAsync runProcess)
    {
        _runProcess = runProcess;
    }

    private static ProcessRunAsync CreateDefaultRunner()
    {
        var processRunner = new ProcessRunner();
        return (spec, cancellationToken) => processRunner.RunAsync(spec, cancellationToken);
    }

    public async Task<SarifAnalysisResult> RunAsync(
        CheckConfig check,
        DistillRunContext context,
        string checkDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(checkDirectory);
        var outputPath = Path.Combine(checkDirectory, "analysis.sarif");
        var preRunSnapshot = CaptureEligibleFallbackSnapshot(context.WorkspaceRoot);
        var parsed = DotnetCommandParser.Parse(check.Command);
        var process = await _runProcess(
            new ProcessSpec(
                FileName: "dotnet",
                Arguments: DotnetCommandParser.ToArgumentList(parsed),
                WorkingDirectory: context.WorkspaceRoot,
                Timeout: TimeSpan.FromSeconds(check.Timeout),
                StdoutPath: Path.Combine(checkDirectory, "stdout.log"),
                StderrPath: Path.Combine(checkDirectory, "stderr.log")),
            cancellationToken).ConfigureAwait(false);

        if (process.Status is ProcessStatus.Canceled)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (process.Status is ProcessStatus.TimedOut or ProcessStatus.FailedToStart)
        {
            var artifactPath = File.Exists(outputPath) ? outputPath : null;
            return new SarifAnalysisResult(
                new[]
                {
                    CreateInfraDiagnostic(
                        process.Status,
                        process.ErrorMessage ?? $"Process ended with status {process.Status}.")
                },
                VerificationStatus.InfraError,
                process.ExitCode,
                artifactPath,
                process.Status);
        }

        var candidate = ResolveCompletedArtifact(context.WorkspaceRoot, outputPath, preRunSnapshot);
        if (candidate is null)
        {
            var diagnostic = DistillDiagnostic.Create(
                id: "sarif-missing",
                kind: DiagnosticKind.Infrastructure,
                severity: DiagnosticSeverity.Error,
                source: "sarif",
                code: "SARIF_MISSING",
                message: "Analysis completed without producing a SARIF artifact.",
                provenance: DiagnosticProvenance.RawFallback,
                confidence: 1.0);
            return new SarifAnalysisResult(
                new[] { diagnostic },
                VerificationStatus.Uncertain,
                process.ExitCode,
                null,
                process.Status);
        }

        var diagnostics = SarifDiagnosticMapper.Map(candidate);
        diagnostics = StampCheckTargetProject(diagnostics, check.Command);
        var status = diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            || process.ExitCode != 0
                ? VerificationStatus.Fail
                : VerificationStatus.Pass;

        await File.WriteAllTextAsync(
            Path.Combine(checkDirectory, "diagnostics.json"),
            JsonSerializer.Serialize(diagnostics, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken).ConfigureAwait(false);

        return new SarifAnalysisResult(
            diagnostics,
            status,
            process.ExitCode,
            candidate,
            process.Status);
    }

    private static DistillDiagnostic CreateInfraDiagnostic(ProcessStatus status, string message)
        => DistillDiagnostic.Create(
            id: "sarif-infra-1",
            kind: DiagnosticKind.Infrastructure,
            severity: DiagnosticSeverity.Error,
            source: "sarif",
            code: status.ToString().ToUpperInvariant(),
            message: message,
            provenance: DiagnosticProvenance.RawFallback,
            confidence: 1.0);

    /// <summary>
    /// 프로젝트 범위 검사(csproj/fsproj 명령 대상)는 자체 Project가 없는
    /// 진단에 자신의 프로젝트를 찍는다. SARIF
    /// 생산자는 properties.project를 거의 내지 않으므로 검사 대상이
    /// 복제본의 권위 있는 프로젝트 출처다. 솔루션 전체 검사는
    /// 프로젝트를 절대 찍지 않는다(그러면 프로젝트 범위가 위조된다).
    /// </summary>
    private static IReadOnlyList<DistillDiagnostic> StampCheckTargetProject(
        IReadOnlyList<DistillDiagnostic> diagnostics,
        string command)
    {
        string? target;
        try
        {
            target = Distill.Core.Planning.DotnetCommandParser.Parse(command).Target;
        }
        catch (ArgumentException)
        {
            return diagnostics;
        }

        var targetPath = (target ?? string.Empty).Replace('\\', '/');
        if (targetPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
            || targetPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            return diagnostics;
        }

        if (!targetPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            && !targetPath.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase))
        {
            return diagnostics;
        }

        var project = Path.GetFileNameWithoutExtension(targetPath);
        return diagnostics
            .Select(diagnostic => string.IsNullOrWhiteSpace(diagnostic.Project)
                ? diagnostic with { Project = project }
                : diagnostic)
            .ToArray();
    }

    private static string? ResolveCompletedArtifact(
        string workspaceRoot,
        string outputPath,
        IReadOnlyDictionary<string, SarifFingerprint> preRunSnapshot)
    {
        if (File.Exists(outputPath))
        {
            return outputPath;
        }

        return FindChangedFallbackSarif(workspaceRoot, preRunSnapshot);
    }

    private static Dictionary<string, SarifFingerprint> CaptureEligibleFallbackSnapshot(string workspaceRoot)
    {
        var snapshot = new Dictionary<string, SarifFingerprint>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in EnumerateEligibleFallbackSarifFiles(workspaceRoot))
        {
            snapshot[NormalizePath(path)] = CreateFingerprint(path);
        }

        return snapshot;
    }

    private static string? FindChangedFallbackSarif(
        string workspaceRoot,
        IReadOnlyDictionary<string, SarifFingerprint> preRunSnapshot)
        => EnumerateEligibleFallbackSarifFiles(workspaceRoot)
            .Where(path =>
            {
                var normalizedPath = NormalizePath(path);
                var currentFingerprint = CreateFingerprint(path);
                return !preRunSnapshot.TryGetValue(normalizedPath, out var priorFingerprint)
                       || !priorFingerprint.Equals(currentFingerprint);
            })
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

    private static IEnumerable<string> EnumerateEligibleFallbackSarifFiles(string workspaceRoot)
    {
        if (!Directory.Exists(workspaceRoot))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(workspaceRoot, "*.sarif", SearchOption.AllDirectories))
        {
            if (IsUnderDistillRoot(workspaceRoot, path))
            {
                continue;
            }

            yield return path;
        }
    }

    private static bool IsUnderDistillRoot(string workspaceRoot, string path)
    {
        var distillRoot = Path.Combine(
            Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            RunArtifactLayout.DistillRootFolder);
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(distillRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || string.Equals(fullPath, distillRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static SarifFingerprint CreateFingerprint(string path)
    {
        var info = new FileInfo(path);
        return new SarifFingerprint(NormalizePath(path), info.LastWriteTimeUtc, info.Length);
    }

    private static string NormalizePath(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private readonly record struct SarifFingerprint(string Path, DateTime LastWriteTimeUtc, long Length);
}
