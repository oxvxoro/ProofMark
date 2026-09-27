using System.Text.Json;
using Distill.Core.Abstractions;
using Distill.Core.Diagnostics;
using Distill.Core.Planning;
using Distill.Core.Redaction;
using Distill.Core.Runs;
using Distill.Git;
using Distill.Reporting;

namespace Distill.Tests.Cli;

// Brief 01 — 오케스트레이터가 검사별 원시 집계 상태와
// 충분성 조정 후 최종 상태를 잇는 배선의 회귀 범위다.
// 그 최종 상태는 normalized.json, run.json,
// 콘솔 출력, 종료 코드에 일관되게 반영되어야 한다.
public class VerifyOrchestratorStatusConsistencyTests
{
    [Fact]
    public void StructuredFail_FinalStatusIsFailEverywhere()
    {
        var checks = BuildDiagnosticFail();
        var (pack, normalized) = RunPipeline(VerificationStatus.Fail, checks);

        Assert.Equal(VerificationStatus.Fail, pack.Status);
        Assert.Equal(VerificationStatus.Fail, normalized.Status);
        Assert.Equal(1, Distill.Cli.ExitCodeMapper.FromStatus(pack.Status));
    }

    [Fact]
    public void InsufficientFail_BecomesUncertainEverywhere()
    {
        var checks = BuildInsufficientFail();
        var (pack, normalized) = RunPipeline(VerificationStatus.Fail, checks);

        Assert.Equal(VerificationStatus.Uncertain, pack.Status);
        Assert.Equal(VerificationStatus.Uncertain, normalized.Status);
        Assert.Equal(4, Distill.Cli.ExitCodeMapper.FromStatus(pack.Status));
    }

    [Fact]
    public void InfraError_StaysInfraErrorEverywhere()
    {
        var checks = new[]
        {
            new CheckRunResult(
                "build",
                "build",
                VerificationStatus.InfraError,
                null,
                Array.Empty<DistillDiagnostic>(),
                "msbuild-binlog",
                "/repo/.distill/runs/x/build",
                TimeSpan.FromSeconds(1))
        };

        var (pack, normalized) = RunPipeline(VerificationStatus.InfraError, checks);

        Assert.Equal(VerificationStatus.InfraError, pack.Status);
        Assert.Equal(VerificationStatus.InfraError, normalized.Status);
        Assert.Equal(3, Distill.Cli.ExitCodeMapper.FromStatus(pack.Status));
    }

    [Fact]
    public void Pass_StaysPassEverywhere()
    {
        var checks = new[]
        {
            new CheckRunResult(
                "build",
                "build",
                VerificationStatus.Pass,
                0,
                Array.Empty<DistillDiagnostic>(),
                "msbuild-binlog",
                "/repo/.distill/runs/x/build",
                TimeSpan.FromSeconds(1))
        };

        var (pack, normalized) = RunPipeline(VerificationStatus.Pass, checks);

        Assert.Equal(VerificationStatus.Pass, pack.Status);
        Assert.Equal(VerificationStatus.Pass, normalized.Status);
        Assert.Equal(0, Distill.Cli.ExitCodeMapper.FromStatus(pack.Status));
    }

    [Fact]
    public void MixedFailAndUncertainChecks_RawStatusResolvesToFail()
    {
        // Brief 01 / Assumption A1: 한 검사의 확정된 Fail은 원시 전체
        // 상태를 계산할 때 다른 검사의 Uncertain에 가려지면 안 된다.
        // 그 상태는 VerificationReportBuilder.Build로 들어간다.
        var failCheck = BuildDiagnosticFail()[0];
        var uncertainCheck = new CheckRunResult(
            "unit",
            "test",
            VerificationStatus.Uncertain,
            1,
            Array.Empty<DistillDiagnostic>(),
            "generic",
            null,
            TimeSpan.FromSeconds(1));

        var rawStatus = Distill.Core.Planning.CheckExecutorCoordinator.ResolveOverallStatus(
            new[] { failCheck, uncertainCheck });

        Assert.Equal(VerificationStatus.Fail, rawStatus);

        var (pack, normalized) = RunPipeline(rawStatus, new[] { failCheck, uncertainCheck });

        Assert.Equal(VerificationStatus.Fail, pack.Status);
        Assert.Equal(VerificationStatus.Fail, normalized.Status);
        Assert.Equal(1, Distill.Cli.ExitCodeMapper.FromStatus(pack.Status));
    }

    [Fact]
    public void PerCheckFail_CanCoexistWithRunLevelUncertain()
    {
        var checks = BuildInsufficientFail();
        var (pack, _) = RunPipeline(VerificationStatus.Fail, checks);

        Assert.Equal(VerificationStatus.Fail, checks[0].Status);
        Assert.Equal(VerificationStatus.Uncertain, pack.Status);
    }

    [Fact]
    public async Task DiagnosticsCommandStyleParse_SucceedsAgainstFinalStatusNormalizedJson()
    {
        var checks = BuildInsufficientFail();
        var (pack, normalized) = RunPipeline(VerificationStatus.Fail, checks);
        var gitSnapshot = new GitChangeSnapshot(
            string.Empty,
            Array.Empty<string>(),
            Array.Empty<ChangedHunk>(),
            string.Empty);

        var workspace = Path.Combine(Path.GetTempPath(), $"distill-normalized-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        try
        {
            var pipeline = new DistillationPipeline();
            await pipeline.WriteNormalizedAsync(
                normalized,
                workspace,
                gitSnapshot,
                CancellationToken.None,
                new SecretRedactor());

            var normalizedPath = RunArtifactLayout.GetNormalizedPath(workspace);
            Assert.True(File.Exists(normalizedPath));

            var json = await File.ReadAllTextAsync(normalizedPath);
            using var document = JsonDocument.Parse(json);
            var statusText = document.RootElement.GetProperty("status").GetString();

            Assert.Equal(pack.Status.ToString(), statusText);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task WriteNormalizedAsync_ConcurrentWriters_ProduceValidJsonWithoutCorruption()
    {
        // Phase 3: DistillationPipeline.WriteNormalizedAsync는 이제 공유
        // AtomicFileWriter(프로세스 간 잠금 포함)를 사설
        // temp+move 구현 대신 재사용한다. 같은 경로에 동시에 쓰는 경우에도
        // 파싱 가능한 normalized.json 하나만 남는지 확인한다.
        var checks = BuildDiagnosticFail();
        var (_, normalized) = RunPipeline(VerificationStatus.Fail, checks);
        var gitSnapshot = new GitChangeSnapshot(
            string.Empty,
            Array.Empty<string>(),
            Array.Empty<ChangedHunk>(),
            string.Empty);

        var workspace = Path.Combine(Path.GetTempPath(), $"distill-normalized-concurrent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        try
        {
            var pipeline = new DistillationPipeline();
            var writers = Enumerable.Range(0, 8)
                .Select(_ => pipeline.WriteNormalizedAsync(
                    normalized,
                    workspace,
                    gitSnapshot,
                    CancellationToken.None,
                    new SecretRedactor()));

            await Task.WhenAll(writers);

            var normalizedPath = RunArtifactLayout.GetNormalizedPath(workspace);
            Assert.True(File.Exists(normalizedPath));

            var json = await File.ReadAllTextAsync(normalizedPath);
            using var document = JsonDocument.Parse(json);
            Assert.Equal(VerificationStatus.Fail.ToString(), document.RootElement.GetProperty("status").GetString());

            var leftoverTempFiles = Directory.EnumerateFiles(workspace, "*.tmp");
            Assert.Empty(leftoverTempFiles);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static (VerificationPack Pack, NormalizedEvidence Normalized) RunPipeline(
        VerificationStatus rawStatus,
        IReadOnlyList<CheckRunResult> checks)
    {
        var context = new DistillRunContext
        {
            RunId = "d-status-consistency",
            WorkspaceRoot = "/repo",
            RunDirectory = "/repo/.distill/runs/d-status-consistency",
            Profile = "quick"
        };

        var gitSnapshot = new GitChangeSnapshot(
            string.Empty,
            Array.Empty<string>(),
            Array.Empty<ChangedHunk>(),
            string.Empty);

        // VerifyOrchestrator.RunAsync를 따른다. 보고서 팩을 먼저 만들고,
        // 그다음 pack.Status(rawStatus가 아님)를 정규화 증거
        // 파이프라인에 넣어 VerifyOrchestrator가 normalized.json으로 남긴다.
        var pack = VerificationReportBuilder.Build(
            rawStatus,
            context,
            checks,
            gitSnapshot,
            new ReportOptions(ReportOutputFormat.Compact));

        var pipeline = new DistillationPipeline();
        var normalized = pipeline.Normalize(
            pack.Status,
            checks,
            checks.SelectMany(check => check.Diagnostics).ToList(),
            gitSnapshot,
            context.WorkspaceRoot);

        return (pack, normalized);
    }

    private static CheckRunResult[] BuildDiagnosticFail()
    {
        var diagnostic = DistillDiagnostic.Create(
            id: "build-error-1",
            kind: DiagnosticKind.Build,
            severity: DiagnosticSeverity.Error,
            source: "build",
            code: "CS1002",
            message: "; expected",
            location: new SourceLocation("src/App.cs", 10, 1),
            provenance: DiagnosticProvenance.MsBuildBinaryLog,
            confidence: 1.0);

        return new[]
        {
            new CheckRunResult(
                "build",
                "build",
                VerificationStatus.Fail,
                1,
                new[] { diagnostic },
                "msbuild-binlog",
                "/repo/.distill/runs/x/build/build.binlog",
                TimeSpan.FromSeconds(2))
        };
    }

    private static CheckRunResult[] BuildInsufficientFail()
    {
        // Fail 상태인데 수집된 빌드 진단이 없다. SufficiencyGuard가
        // 이를 Uncertain으로 강제한다(SufficiencyGuard.Assess "no build diagnostics" 참고).
        return new[]
        {
            new CheckRunResult(
                "build",
                "build",
                VerificationStatus.Fail,
                1,
                Array.Empty<DistillDiagnostic>(),
                "msbuild-binlog",
                "/repo/.distill/runs/x/build/build.binlog",
                TimeSpan.FromSeconds(2))
        };
    }
}
