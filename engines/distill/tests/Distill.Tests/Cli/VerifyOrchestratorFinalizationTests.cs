using Distill.Cli.Services;
using Distill.Core.Abstractions;
using Distill.Core.Config;
using Distill.Core.Runs;

namespace Distill.Tests.Cli;

// Brief 04 — 초기 run.json이 쓰인 뒤, 취소가 아닌 예외
// (예: 실행 생성 뒤에 풀린 잘못된 프로필 이름)는 미완성
// 매니페스트 대신 확정된 매니페스트(FinishedAt 설정, Status=InfraError)를 남겨야 한다.
// 그래야 실행/상태 소비자가 "아직 실행 중이거나 매니페스트 이전에 죽었음"과
// "시작한 뒤에 죽었지만 실패인 것은 안다"를 구별할 수 있다.
public class VerifyOrchestratorFinalizationTests
{
    [Fact]
    public async Task RunAsync_NonCancellationExceptionAfterInitialManifest_WritesFinalizedInfraErrorManifest()
    {
        var workspace = CreateWorkspace();
        try
        {
            var config = new DistillConfig();
            var orchestrator = new VerifyOrchestrator();

            var exception = await Record.ExceptionAsync(() => orchestrator.RunAsync(
                workspace,
                config,
                "profile-that-does-not-exist",
                new ReportOptions(),
                CancellationToken.None));

            Assert.IsType<KeyNotFoundException>(exception);

            var runsRoot = RunArtifactLayout.GetRunsRoot(workspace);
            var runDirectory = Assert.Single(Directory.EnumerateDirectories(runsRoot));
            var manifestPath = RunArtifactLayout.GetRunManifestPath(runDirectory);
            Assert.True(File.Exists(manifestPath));

            var manifest = System.Text.Json.JsonSerializer.Deserialize<RunManifest>(
                await File.ReadAllTextAsync(manifestPath),
                new System.Text.Json.JsonSerializerOptions
                {
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                });

            Assert.NotNull(manifest);
            Assert.NotNull(manifest!.FinishedAt);
            Assert.Equal(VerificationStatus.InfraError, manifest.Status);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_NonCancellationExceptionAfterInitialManifest_DoesNotUpdateStatePointer()
    {
        var workspace = CreateWorkspace();
        try
        {
            var config = new DistillConfig();
            var orchestrator = new VerifyOrchestrator();

            await Record.ExceptionAsync(() => orchestrator.RunAsync(
                workspace,
                config,
                "profile-that-does-not-exist",
                new ReportOptions(),
                CancellationToken.None));

            Assert.False(File.Exists(RunArtifactLayout.GetStatePath(workspace)));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_CancellationAfterInitialManifest_WritesFinalizedInfraErrorManifest()
    {
        var workspace = CreateWorkspace();
        using var cts = new CancellationTokenSource();
        try
        {
            var config = new DistillConfig
            {
                Profiles =
                {
                    ["quick"] = new ProfileDefinition { Checks = ["build"] }
                },
                Checks =
                {
                    ["build"] = new CheckConfig
                    {
                        Kind = "build",
                        Command = "dotnet build App.sln",
                        Timeout = 600
                    }
                }
            };

            var orchestrator = new VerifyOrchestrator();
            var runTask = orchestrator.RunAsync(
                workspace,
                config,
                "quick",
                new ReportOptions(),
                cts.Token);

            var runsRoot = RunArtifactLayout.GetRunsRoot(workspace);
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if (Directory.Exists(runsRoot))
                {
                    var runDirectory = Directory.EnumerateDirectories(runsRoot).FirstOrDefault();
                    if (runDirectory is not null
                        && File.Exists(RunArtifactLayout.GetRunManifestPath(runDirectory)))
                    {
                        cts.Cancel();
                        break;
                    }
                }

                await Task.Delay(20);
            }

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);

            var runDirectoryPath = Assert.Single(Directory.EnumerateDirectories(runsRoot));
            var manifest = System.Text.Json.JsonSerializer.Deserialize<RunManifest>(
                await File.ReadAllTextAsync(RunArtifactLayout.GetRunManifestPath(runDirectoryPath)),
                new System.Text.Json.JsonSerializerOptions
                {
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                });

            Assert.NotNull(manifest);
            Assert.NotNull(manifest!.FinishedAt);
            Assert.Equal(VerificationStatus.InfraError, manifest.Status);
            Assert.False(File.Exists(RunArtifactLayout.GetStatePath(workspace)));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_EmptyPlannedChecks_ThrowsAndFinalizesInfraErrorManifest()
    {
        var workspace = CreateWorkspace();
        try
        {
            var config = new DistillConfig
            {
                Profiles =
                {
                    ["empty"] = new ProfileDefinition { Checks = ["orphan"] }
                },
                Checks =
                {
                    ["orphan"] = new CheckConfig
                    {
                        Kind = "build",
                        Command = "dotnet build App.sln"
                    }
                }
            };

            // Validate 우회: 프로필이 AddCheck가 중복 제거하여 계획된 검사가 0개가 되는 검사를 참조하면
            // 만들기가 어렵다. 대신 검사는 있으나 빈 Checks 목록 때문에 Plan이 빈 프로필을 쓴다.
            config.Profiles["empty"] = new ProfileDefinition();

            var orchestrator = new VerifyOrchestrator();
            var exception = await Record.ExceptionAsync(() => orchestrator.RunAsync(
                workspace,
                config,
                "empty",
                new ReportOptions(),
                CancellationToken.None));

            Assert.IsType<InvalidOperationException>(exception);
            Assert.Contains("no planned checks", exception!.Message, StringComparison.OrdinalIgnoreCase);

            var runsRoot = RunArtifactLayout.GetRunsRoot(workspace);
            var runDirectory = Assert.Single(Directory.EnumerateDirectories(runsRoot));
            var manifest = System.Text.Json.JsonSerializer.Deserialize<RunManifest>(
                await File.ReadAllTextAsync(RunArtifactLayout.GetRunManifestPath(runDirectory)),
                new System.Text.Json.JsonSerializerOptions
                {
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                });

            Assert.NotNull(manifest);
            Assert.NotNull(manifest!.FinishedAt);
            Assert.Equal(VerificationStatus.InfraError, manifest.Status);
            Assert.False(File.Exists(RunArtifactLayout.GetStatePath(workspace)));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static string CreateWorkspace()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"distill-finalization-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        return workspace;
    }
}
