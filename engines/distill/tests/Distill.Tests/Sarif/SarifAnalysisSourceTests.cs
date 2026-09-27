using Distill.Analysis.Sarif;
using Distill.Core.Config;
using Distill.Core.Diagnostics;
using Distill.Core.Runs;
using Distill.Runner;

namespace Distill.Tests.Sarif;

public class SarifAnalysisSourceTests
{
    private static string FixturePath(string relativePath)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "fixtures", "sarif", relativePath));

    [Fact]
    public async Task RunAsync_CompletedWithoutCurrentSarif_DoesNotReusePriorDistillRunSarif()
    {
        var workspace = CreateWorkspace();
        try
        {
            var stalePath = Path.Combine(
                workspace,
                RunArtifactLayout.DistillRootFolder,
                "runs",
                "old-run",
                "analysis",
                "analysis.sarif");
            Directory.CreateDirectory(Path.GetDirectoryName(stalePath)!);
            File.Copy(FixturePath("single-warning.sarif"), stalePath);

            var source = CreateSource(ProcessStatus.Completed, 0);
            var result = await RunAsync(source, workspace);

            Assert.Equal(VerificationStatus.Uncertain, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SARIF_MISSING");
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_CompletedWithoutChangedFallback_DoesNotReusePreexistingWorkspaceSarif()
    {
        var workspace = CreateWorkspace();
        try
        {
            var stalePath = Path.Combine(workspace, "existing.sarif");
            File.Copy(FixturePath("single-warning.sarif"), stalePath);

            var source = CreateSource(ProcessStatus.Completed, 0);
            var result = await RunAsync(source, workspace);

            Assert.Equal(VerificationStatus.Uncertain, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SARIF_MISSING");
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_CompletedWithChangedWorkspaceFallback_UsesCurrentFallback()
    {
        var workspace = CreateWorkspace();
        try
        {
            var fallbackPath = Path.Combine(workspace, "fallback.sarif");
            var source = new SarifAnalysisSource((_, _) =>
            {
                File.Copy(FixturePath("single-warning.sarif"), fallbackPath, overwrite: true);
                return Task.FromResult(new ProcessResult(0, TimeSpan.Zero, ProcessStatus.Completed, null, null));
            });

            var result = await RunAsync(source, workspace);

            Assert.Equal(VerificationStatus.Pass, result.Status);
            Assert.Equal(fallbackPath, result.ArtifactPath);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_FailedToStart_WithStaleSarif_ReturnsInfraError()
    {
        var workspace = CreateWorkspace();
        try
        {
            var stalePath = Path.Combine(workspace, "existing.sarif");
            File.Copy(FixturePath("single-warning.sarif"), stalePath);

            var source = CreateSource(ProcessStatus.FailedToStart, null, "Process failed to start.");
            var result = await RunAsync(source, workspace);

            Assert.Equal(VerificationStatus.InfraError, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "FAILEDTOSTART");
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_TimedOut_WithStaleSarif_ReturnsInfraError()
    {
        var workspace = CreateWorkspace();
        try
        {
            var stalePath = Path.Combine(workspace, "existing.sarif");
            File.Copy(FixturePath("single-warning.sarif"), stalePath);

            var source = CreateSource(ProcessStatus.TimedOut, null, "Process timed out.");
            var result = await RunAsync(source, workspace);

            Assert.Equal(VerificationStatus.InfraError, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "TIMEDOUT");
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_CallerCancellation_Propagates()
    {
        var workspace = CreateWorkspace();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var source = new SarifAnalysisSource((_, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ProcessResult(null, TimeSpan.Zero, ProcessStatus.Canceled, null, null));
        });

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            RunAsync(source, workspace, cts.Token));
    }

    [Fact]
    public async Task RunAsync_ProjectScopedCheck_StampsWithTargetProject()
    {
        var workspace = CreateWorkspace();
        try
        {
            var outputPath = Path.Combine(workspace, "analysis.sarif");
            var source = new SarifAnalysisSource((_, _) =>
            {
                File.Copy(FixturePath("single-warning.sarif"), outputPath, overwrite: true);
                return Task.FromResult(new ProcessResult(0, TimeSpan.Zero, ProcessStatus.Completed, null, null));
            });

            var runDirectory = Path.Combine(workspace, RunArtifactLayout.DistillRootFolder, "runs", "test-run");
            var checkDirectory = RunArtifactLayout.GetCheckDirectory(runDirectory, "analysis");
            var context = new DistillRunContext
            {
                WorkspaceRoot = workspace,
                RunDirectory = runDirectory,
                RunId = "test-run"
            };
            var check = new CheckConfig
            {
                Kind = "analysis",
                Command = "dotnet build src/Proof.Core/Proof.Core.csproj",
                Timeout = 300
            };

            var result = await source.RunAsync(check, context, checkDirectory, CancellationToken.None);

            // 검사 대상은 프로젝트다. 자체
            // Project 필드가 없는 진단에는 대상 프로젝트를 찍는다(distill 전용
            // SARIF 경로는 Proof 쪽 수정 없이 프로젝트 출처를 유지한다).
            Assert.Equal("Proof.Core", Assert.Single(result.Diagnostics).Project);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_SolutionScopedCheck_DoesNotStampProject()
    {
        var workspace = CreateWorkspace();
        try
        {
            var outputPath = Path.Combine(workspace, "analysis.sarif");
            var source = new SarifAnalysisSource((_, _) =>
            {
                File.Copy(FixturePath("single-warning.sarif"), outputPath, overwrite: true);
                return Task.FromResult(new ProcessResult(0, TimeSpan.Zero, ProcessStatus.Completed, null, null));
            });

            var runDirectory = Path.Combine(workspace, RunArtifactLayout.DistillRootFolder, "runs", "test-run");
            var checkDirectory = RunArtifactLayout.GetCheckDirectory(runDirectory, "analysis");
            var context = new DistillRunContext
            {
                WorkspaceRoot = workspace,
                RunDirectory = runDirectory,
                RunId = "test-run"
            };
            var check = new CheckConfig
            {
                Kind = "analysis",
                Command = "dotnet build Proof.slnx",
                Timeout = 300
            };

            var result = await source.RunAsync(check, context, checkDirectory, CancellationToken.None);

            // 솔루션 전체 검사는 프로젝트 범위를 절대 주장하지 않는다.
            Assert.Null(Assert.Single(result.Diagnostics).Project);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static SarifAnalysisSource CreateSource(
        ProcessStatus status,
        int? exitCode,
        string? errorMessage = null)
        => new((_, _) =>
            Task.FromResult(new ProcessResult(exitCode, TimeSpan.Zero, status, null, null, errorMessage)));

    private static async Task<SarifAnalysisResult> RunAsync(
        SarifAnalysisSource source,
        string workspace,
        CancellationToken cancellationToken = default)
    {
        var runDirectory = Path.Combine(workspace, RunArtifactLayout.DistillRootFolder, "runs", "test-run");
        var checkDirectory = RunArtifactLayout.GetCheckDirectory(runDirectory, "analysis");
        var context = new DistillRunContext
        {
            WorkspaceRoot = workspace,
            RunDirectory = runDirectory,
            RunId = "test-run"
        };
        var check = new CheckConfig
        {
            Kind = "analysis",
            Command = "dotnet build",
            Timeout = 300
        };

        return await source.RunAsync(check, context, checkDirectory, cancellationToken);
    }

    private static string CreateWorkspace()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"distill-sarif-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        return workspace;
    }
}
