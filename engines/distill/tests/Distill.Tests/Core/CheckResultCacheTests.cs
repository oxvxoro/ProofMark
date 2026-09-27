using Distill.Core.Config;
using Distill.Core.Diagnostics;
using Distill.Core.Planning;
using Distill.Core.Runs;
using Distill.Tests.TestSupport;

namespace Distill.Tests.Core;

public class CheckResultCacheTests
{
    [Fact]
    public void TryRestore_AfterStore_ReturnsMatchingPassResultWithoutReexecution()
    {
        using var workspace = GitWorkspace.Create();
        var runDirectory = Path.Combine(workspace.Root, ".distill", "runs", "d-test");
        var checkDirectory = Path.Combine(runDirectory, "build");
        Directory.CreateDirectory(checkDirectory);
        File.WriteAllText(Path.Combine(checkDirectory, "stdout.log"), "cached output");

        var check = BuildCheck();
        var artifactPointer = Path.Combine(checkDirectory, "stdout.log");
        var original = new CheckRunResult(
            "build",
            "build",
            VerificationStatus.Pass,
            0,
            Array.Empty<DistillDiagnostic>(),
            "msbuild-binlog",
            artifactPointer,
            TimeSpan.FromSeconds(2));

        var context = new DistillRunContext
        {
            RunId = "d-test",
            WorkspaceRoot = workspace.Root,
            RunDirectory = runDirectory,
            Profile = "quick"
        };

        CheckResultCache.TryStore(check, context, original);

        var restoreContext = new DistillRunContext
        {
            RunId = "d-test-2",
            WorkspaceRoot = workspace.Root,
            RunDirectory = Path.Combine(workspace.Root, ".distill", "runs", "d-test-2"),
            Profile = "quick"
        };

        Assert.True(CheckResultCache.TryRestore(check, restoreContext, out var restored));
        Assert.NotNull(restored);
        Assert.Equal(VerificationStatus.Pass, restored!.Status);
        Assert.Equal(
            Path.Combine(restoreContext.RunDirectory, "build", "stdout.log"),
            restored.ArtifactPointer);
        Assert.True(File.Exists(Path.Combine(restoreContext.RunDirectory, "build", "stdout.log")));
    }

    [Fact]
    public void TryRestore_WhenTrackedFileChangedWithoutNewCommit_ReturnsFalse()
    {
        using var workspace = GitWorkspace.Create();
        var runDirectory = Path.Combine(workspace.Root, ".distill", "runs", "d-test");
        var checkDirectory = Path.Combine(runDirectory, "build");
        Directory.CreateDirectory(checkDirectory);
        File.WriteAllText(Path.Combine(checkDirectory, "stdout.log"), "cached output");

        var check = BuildCheck();
        var context = new DistillRunContext
        {
            RunId = "d-test",
            WorkspaceRoot = workspace.Root,
            RunDirectory = runDirectory,
            Profile = "quick"
        };

        CheckResultCache.TryStore(
            check,
            context,
            new CheckRunResult(
                "build",
                "build",
                VerificationStatus.Pass,
                0,
                Array.Empty<DistillDiagnostic>(),
                "msbuild-binlog",
                Path.Combine(checkDirectory, "stdout.log"),
                TimeSpan.FromSeconds(2)));

        File.WriteAllText(Path.Combine(workspace.Root, "source.txt"), "changed without commit");

        var restoreContext = new DistillRunContext
        {
            RunId = "d-test-2",
            WorkspaceRoot = workspace.Root,
            RunDirectory = Path.Combine(workspace.Root, ".distill", "runs", "d-test-2"),
            Profile = "quick"
        };

        Assert.False(CheckResultCache.TryRestore(check, restoreContext, out var restored));
        Assert.Null(restored);
    }

    [Fact]
    public void TryRestore_WhenUntrackedCsFileAdded_ReturnsFalse()
    {
        using var workspace = GitWorkspace.Create();
        var runDirectory = Path.Combine(workspace.Root, ".distill", "runs", "d-test");
        var checkDirectory = Path.Combine(runDirectory, "build");
        Directory.CreateDirectory(checkDirectory);
        File.WriteAllText(Path.Combine(checkDirectory, "stdout.log"), "cached output");

        var check = BuildCheck();
        var context = new DistillRunContext
        {
            RunId = "d-test",
            WorkspaceRoot = workspace.Root,
            RunDirectory = runDirectory,
            Profile = "quick"
        };

        CheckResultCache.TryStore(
            check,
            context,
            new CheckRunResult(
                "build",
                "build",
                VerificationStatus.Pass,
                0,
                Array.Empty<DistillDiagnostic>(),
                "msbuild-binlog",
                Path.Combine(checkDirectory, "stdout.log"),
                TimeSpan.FromSeconds(2)));

        File.WriteAllText(Path.Combine(workspace.Root, "NewFeature.cs"), "public class NewFeature { }");

        var restoreContext = new DistillRunContext
        {
            RunId = "d-test-2",
            WorkspaceRoot = workspace.Root,
            RunDirectory = Path.Combine(workspace.Root, ".distill", "runs", "d-test-2"),
            Profile = "quick"
        };

        Assert.False(CheckResultCache.TryRestore(check, restoreContext, out var restored));
        Assert.Null(restored);
    }

    [Fact]
    public async Task ExecuteDependencyAwareAsync_UntrackedCsFileBypassesCacheAndExecutes()
    {
        using var workspace = GitWorkspace.Create();
        var check = BuildCheck();
        var executor = new CountingExecutor();

        var storeContext = new DistillRunContext
        {
            RunId = "d-store",
            WorkspaceRoot = workspace.Root,
            RunDirectory = Path.Combine(workspace.Root, ".distill", "runs", "d-store"),
            Profile = "quick"
        };
        var storeDirectory = Path.Combine(storeContext.RunDirectory, "build");
        Directory.CreateDirectory(storeDirectory);
        File.WriteAllText(Path.Combine(storeDirectory, "stdout.log"), "cached output");
        CheckResultCache.TryStore(
            check,
            storeContext,
            new CheckRunResult(
                "build",
                "build",
                VerificationStatus.Pass,
                0,
                Array.Empty<DistillDiagnostic>(),
                "msbuild-binlog",
                Path.Combine(storeDirectory, "stdout.log"),
                TimeSpan.FromSeconds(1)));

        File.WriteAllText(Path.Combine(workspace.Root, "NewFeature.cs"), "public class NewFeature { }");

        var runContext = new DistillRunContext
        {
            RunId = "d-run",
            WorkspaceRoot = workspace.Root,
            RunDirectory = Path.Combine(workspace.Root, ".distill", "runs", "d-run"),
            Profile = "quick"
        };

        await CheckExecutorCoordinator.ExecuteSequentialAsync(
            executor,
            [check],
            runContext,
            CancellationToken.None);

        Assert.Equal(1, executor.ExecutionCount);
    }

    [Fact]
    public async Task ExecuteDependencyAwareAsync_DirtyWorkspaceBypassesCacheAndExecutes()
    {
        using var workspace = GitWorkspace.Create();
        var check = BuildCheck();
        var executor = new CountingExecutor();

        var storeContext = new DistillRunContext
        {
            RunId = "d-store",
            WorkspaceRoot = workspace.Root,
            RunDirectory = Path.Combine(workspace.Root, ".distill", "runs", "d-store"),
            Profile = "quick"
        };
        var storeDirectory = Path.Combine(storeContext.RunDirectory, "build");
        Directory.CreateDirectory(storeDirectory);
        File.WriteAllText(Path.Combine(storeDirectory, "stdout.log"), "cached output");
        CheckResultCache.TryStore(
            check,
            storeContext,
            new CheckRunResult(
                "build",
                "build",
                VerificationStatus.Pass,
                0,
                Array.Empty<DistillDiagnostic>(),
                "msbuild-binlog",
                Path.Combine(storeDirectory, "stdout.log"),
                TimeSpan.FromSeconds(1)));

        File.WriteAllText(Path.Combine(workspace.Root, "source.txt"), "dirty change");

        var runContext = new DistillRunContext
        {
            RunId = "d-run",
            WorkspaceRoot = workspace.Root,
            RunDirectory = Path.Combine(workspace.Root, ".distill", "runs", "d-run"),
            Profile = "quick"
        };

        await CheckExecutorCoordinator.ExecuteSequentialAsync(
            executor,
            [check],
            runContext,
            CancellationToken.None);

        Assert.Equal(1, executor.ExecutionCount);
    }

    [Fact]
    public void TryRestore_RemapsAbsoluteArtifactPointerToNewRunDirectory()
    {
        using var workspace = GitWorkspace.Create();
        var originalRunDirectory = Path.Combine(workspace.Root, ".distill", "runs", "d-original");
        var originalCheckDirectory = Path.Combine(originalRunDirectory, "build");
        Directory.CreateDirectory(originalCheckDirectory);
        File.WriteAllText(Path.Combine(originalCheckDirectory, "stdout.log"), "cached output");

        var check = BuildCheck();
        var originalContext = new DistillRunContext
        {
            RunId = "d-original",
            WorkspaceRoot = workspace.Root,
            RunDirectory = originalRunDirectory,
            Profile = "quick"
        };

        CheckResultCache.TryStore(
            check,
            originalContext,
            new CheckRunResult(
                "build",
                "build",
                VerificationStatus.Pass,
                0,
                Array.Empty<DistillDiagnostic>(),
                "msbuild-binlog",
                Path.Combine(originalCheckDirectory, "stdout.log"),
                TimeSpan.FromSeconds(2)));

        var restoreContext = new DistillRunContext
        {
            RunId = "d-restore",
            WorkspaceRoot = workspace.Root,
            RunDirectory = Path.Combine(workspace.Root, ".distill", "runs", "d-restore"),
            Profile = "quick"
        };

        Assert.True(CheckResultCache.TryRestore(check, restoreContext, out var restored));
        Assert.NotNull(restored);
        Assert.Equal(
            Path.Combine(restoreContext.RunDirectory, "build", "stdout.log"),
            restored!.ArtifactPointer);
    }

    [Fact]
    public void TryRestore_PreservesDiagnosticFields()
    {
        using var workspace = GitWorkspace.Create();
        var runDirectory = Path.Combine(workspace.Root, ".distill", "runs", "d-test");
        var checkDirectory = Path.Combine(runDirectory, "build");
        Directory.CreateDirectory(checkDirectory);
        File.WriteAllText(Path.Combine(checkDirectory, "stdout.log"), "cached output");

        var diagnostic = DistillDiagnostic.Create(
            id: "build:CS0001",
            kind: DiagnosticKind.Build,
            severity: DiagnosticSeverity.Error,
            source: "build",
            code: "CS0001",
            message: "compiler failed",
            location: new SourceLocation("App.cs", 12, 4),
            project: "App.csproj",
            testName: null,
            exception: new ExceptionEvidence("System.Exception", "boom"),
            frames:
            [
                new StackFrameEvidence("App.cs", 12, "App.Main", false)
            ],
            provenance: DiagnosticProvenance.RawFallback,
            confidence: 0.75,
            properties: new Dictionary<string, string> { ["category"] = "compiler" });

        var check = BuildCheck();
        var context = new DistillRunContext
        {
            RunId = "d-test",
            WorkspaceRoot = workspace.Root,
            RunDirectory = runDirectory,
            Profile = "quick"
        };

        CheckResultCache.TryStore(
            check,
            context,
            new CheckRunResult(
                "build",
                "build",
                VerificationStatus.Pass,
                0,
                [diagnostic],
                "msbuild-binlog",
                Path.Combine(checkDirectory, "stdout.log"),
                TimeSpan.FromSeconds(2)));

        var restoreContext = new DistillRunContext
        {
            RunId = "d-test-2",
            WorkspaceRoot = workspace.Root,
            RunDirectory = Path.Combine(workspace.Root, ".distill", "runs", "d-test-2"),
            Profile = "quick"
        };

        Assert.True(CheckResultCache.TryRestore(check, restoreContext, out var restored));
        Assert.NotNull(restored);
        var restoredDiagnostic = Assert.Single(restored!.Diagnostics);
        Assert.Equal(diagnostic.Project, restoredDiagnostic.Project);
        Assert.Equal(diagnostic.Exception, restoredDiagnostic.Exception);
        Assert.Equal(diagnostic.Frames, restoredDiagnostic.Frames);
        Assert.Equal(diagnostic.Properties, restoredDiagnostic.Properties);
    }

    [Fact]
    public void TryRestore_NestedArtifactDirectories_RoundTrip()
    {
        using var workspace = GitWorkspace.Create();
        var runDirectory = Path.Combine(workspace.Root, ".distill", "runs", "d-test");
        var checkDirectory = Path.Combine(runDirectory, "build");
        var nestedDirectory = Path.Combine(checkDirectory, "nested", "deeper");
        Directory.CreateDirectory(nestedDirectory);
        File.WriteAllText(Path.Combine(checkDirectory, "stdout.log"), "top-level output");
        File.WriteAllText(Path.Combine(nestedDirectory, "detail.log"), "nested output");

        var check = BuildCheck();
        var context = new DistillRunContext
        {
            RunId = "d-test",
            WorkspaceRoot = workspace.Root,
            RunDirectory = runDirectory,
            Profile = "quick"
        };

        CheckResultCache.TryStore(
            check,
            context,
            new CheckRunResult(
                "build",
                "build",
                VerificationStatus.Pass,
                0,
                Array.Empty<DistillDiagnostic>(),
                "msbuild-binlog",
                Path.Combine(checkDirectory, "stdout.log"),
                TimeSpan.FromSeconds(2)));

        var restoreContext = new DistillRunContext
        {
            RunId = "d-test-2",
            WorkspaceRoot = workspace.Root,
            RunDirectory = Path.Combine(workspace.Root, ".distill", "runs", "d-test-2"),
            Profile = "quick"
        };

        Assert.True(CheckResultCache.TryRestore(check, restoreContext, out var restored));
        Assert.NotNull(restored);
        var restoredNestedFile = Path.Combine(restoreContext.RunDirectory, "build", "nested", "deeper", "detail.log");
        Assert.True(File.Exists(restoredNestedFile));
        Assert.Equal("nested output", File.ReadAllText(restoredNestedFile));
    }

    [Fact]
    public void TryRestore_CacheKeyMismatch_ReturnsFalseWithoutThrowing()
    {
        using var workspace = GitWorkspace.Create();
        var check = BuildCheck();
        StorePassCache(workspace, check);
        OverwriteCachedMetadata(
            workspace.Root,
            """
            {
              "schemaVersion": 2,
              "cacheKey": "does-not-match",
              "checkId": "build",
              "kind": "build",
              "status": "Pass",
              "exitCode": 0,
              "sourceId": "msbuild-binlog",
              "artifactPointer": "stdout.log",
              "durationMs": 1000,
              "diagnostics": []
            }
            """);

        Assert.False(TryRestoreWithoutException(check, workspace.Root, out var restored));
        Assert.Null(restored);
    }

    [Fact]
    public void TryRestore_CheckIdMismatch_ReturnsFalseWithoutThrowing()
    {
        using var workspace = GitWorkspace.Create();
        var check = BuildCheck();
        StorePassCache(workspace, check);
        var cacheKey = ReadCachedMetadataCacheKey(workspace.Root);
        OverwriteCachedMetadata(
            workspace.Root,
            $$"""
            {
              "schemaVersion": 2,
              "cacheKey": "{{cacheKey}}",
              "checkId": "different-check",
              "kind": "build",
              "status": "Pass",
              "exitCode": 0,
              "sourceId": "msbuild-binlog",
              "artifactPointer": "stdout.log",
              "durationMs": 1000,
              "diagnostics": []
            }
            """);

        Assert.False(TryRestoreWithoutException(check, workspace.Root, out var restored));
        Assert.Null(restored);
    }

    [Fact]
    public void TryRestore_KindMismatch_ReturnsFalseWithoutThrowing()
    {
        using var workspace = GitWorkspace.Create();
        var check = BuildCheck();
        StorePassCache(workspace, check);
        var cacheKey = ReadCachedMetadataCacheKey(workspace.Root);
        OverwriteCachedMetadata(
            workspace.Root,
            $$"""
            {
              "schemaVersion": 2,
              "cacheKey": "{{cacheKey}}",
              "checkId": "build",
              "kind": "test",
              "status": "Pass",
              "exitCode": 0,
              "sourceId": "msbuild-binlog",
              "artifactPointer": "stdout.log",
              "durationMs": 1000,
              "diagnostics": []
            }
            """);

        Assert.False(TryRestoreWithoutException(check, workspace.Root, out var restored));
        Assert.Null(restored);
    }

    [Fact]
    public void TryRestore_MissingSchemaVersion_TreatedAsMiss()
    {
        using var workspace = GitWorkspace.Create();
        var check = BuildCheck();
        StorePassCache(workspace, check);
        var cacheKey = ReadCachedMetadataCacheKey(workspace.Root);
        OverwriteCachedMetadata(
            workspace.Root,
            $$"""
            {
              "cacheKey": "{{cacheKey}}",
              "checkId": "build",
              "kind": "build",
              "status": "Pass",
              "exitCode": 0,
              "sourceId": "msbuild-binlog",
              "artifactPointer": "stdout.log",
              "durationMs": 1000,
              "diagnostics": []
            }
            """);

        Assert.False(TryRestoreWithoutException(check, workspace.Root, out var restored));
        Assert.Null(restored);
    }

    [Fact]
    public void TryRestore_ValidationFailure_DoesNotDestroyExistingTargetDirectory()
    {
        using var workspace = GitWorkspace.Create();
        var check = BuildCheck();
        StorePassCache(workspace, check);
        OverwriteCachedMetadata(
            workspace.Root,
            """
            {
              "schemaVersion": 2,
              "cacheKey": "mismatched-key",
              "checkId": "build",
              "kind": "build",
              "status": "Pass",
              "exitCode": 0,
              "sourceId": "msbuild-binlog",
              "artifactPointer": "stdout.log",
              "durationMs": 1000,
              "diagnostics": []
            }
            """);

        var restoreContext = new DistillRunContext
        {
            RunId = "d-test-2",
            WorkspaceRoot = workspace.Root,
            RunDirectory = Path.Combine(workspace.Root, ".distill", "runs", "d-test-2"),
            Profile = "quick"
        };
        var preExistingFile = Path.Combine(restoreContext.RunDirectory, "build", "preexisting.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(preExistingFile)!);
        File.WriteAllText(preExistingFile, "must survive a cache validation miss");

        Assert.False(CheckResultCache.TryRestore(check, restoreContext, out var restored));
        Assert.Null(restored);
        Assert.True(File.Exists(preExistingFile));
    }

    [Fact]
    public void TryStore_ConcurrentSameKeyWriters_ProduceSingleValidTarget()
    {
        using var workspace = GitWorkspace.Create();
        var check = BuildCheck();

        var attempts = Enumerable.Range(0, 8).Select(index =>
        {
            var runDirectory = Path.Combine(workspace.Root, ".distill", "runs", $"d-concurrent-{index}");
            var checkDirectory = Path.Combine(runDirectory, "build");
            Directory.CreateDirectory(checkDirectory);
            File.WriteAllText(Path.Combine(checkDirectory, "stdout.log"), $"output-{index}");

            var context = new DistillRunContext
            {
                RunId = $"d-concurrent-{index}",
                WorkspaceRoot = workspace.Root,
                RunDirectory = runDirectory,
                Profile = "quick"
            };

            var result = new CheckRunResult(
                "build",
                "build",
                VerificationStatus.Pass,
                0,
                Array.Empty<DistillDiagnostic>(),
                "msbuild-binlog",
                Path.Combine(checkDirectory, "stdout.log"),
                TimeSpan.FromSeconds(1));

            return (context, result);
        }).ToList();

        Parallel.ForEach(attempts, attempt => CheckResultCache.TryStore(check, attempt.context, attempt.result));

        var cacheKey = ReadCachedMetadataCacheKey(workspace.Root);
        var entryDirectory = RunArtifactLayout.GetCacheEntryDirectory(workspace.Root, cacheKey);
        Assert.True(Directory.Exists(entryDirectory));
        Assert.True(File.Exists(Path.Combine(entryDirectory, "metadata.json")));

        var tempLeftovers = Directory
            .EnumerateDirectories(Path.Combine(workspace.Root, ".distill", "cache"), "*.tmp", SearchOption.TopDirectoryOnly);
        Assert.Empty(tempLeftovers);

        var restoreContext = new DistillRunContext
        {
            RunId = "d-restore-after-race",
            WorkspaceRoot = workspace.Root,
            RunDirectory = Path.Combine(workspace.Root, ".distill", "runs", "d-restore-after-race"),
            Profile = "quick"
        };

        Assert.True(CheckResultCache.TryRestore(check, restoreContext, out var restored));
        Assert.NotNull(restored);
        Assert.Equal(VerificationStatus.Pass, restored!.Status);
    }

    [Fact]
    public void TryRestore_InvalidStatus_ReturnsFalseWithoutThrowing()
    {
        using var workspace = GitWorkspace.Create();
        var check = BuildCheck();
        StorePassCache(workspace, check);
        OverwriteCachedMetadata(
            workspace.Root,
            """
            {
              "cacheKey": "invalid",
              "checkId": "build",
              "kind": "build",
              "status": "NotAStatus",
              "exitCode": 0,
              "sourceId": "msbuild-binlog",
              "artifactPointer": "stdout.log",
              "durationMs": 1000,
              "diagnostics": []
            }
            """);

        Assert.False(TryRestoreWithoutException(check, workspace.Root, out var restored));
        Assert.Null(restored);
    }

    [Fact]
    public void TryRestore_NestedArtifactPointer_RemapsToNestedLocationInTargetDirectory()
    {
        using var workspace = GitWorkspace.Create();
        var runDirectory = Path.Combine(workspace.Root, ".distill", "runs", "d-test");
        var checkDirectory = Path.Combine(runDirectory, "build");
        var nestedDirectory = Path.Combine(checkDirectory, "nested", "deeper");
        Directory.CreateDirectory(nestedDirectory);
        File.WriteAllText(Path.Combine(nestedDirectory, "detail.log"), "nested output");

        var check = BuildCheck();
        var context = new DistillRunContext
        {
            RunId = "d-test",
            WorkspaceRoot = workspace.Root,
            RunDirectory = runDirectory,
            Profile = "quick"
        };

        CheckResultCache.TryStore(
            check,
            context,
            new CheckRunResult(
                "build",
                "build",
                VerificationStatus.Pass,
                0,
                Array.Empty<DistillDiagnostic>(),
                "msbuild-binlog",
                Path.Combine(nestedDirectory, "detail.log"),
                TimeSpan.FromSeconds(2)));

        var restoreContext = new DistillRunContext
        {
            RunId = "d-test-2",
            WorkspaceRoot = workspace.Root,
            RunDirectory = Path.Combine(workspace.Root, ".distill", "runs", "d-test-2"),
            Profile = "quick"
        };

        Assert.True(CheckResultCache.TryRestore(check, restoreContext, out var restored));
        Assert.NotNull(restored);
        Assert.Equal(
            Path.Combine(restoreContext.RunDirectory, "build", "nested", "deeper", "detail.log"),
            restored!.ArtifactPointer);
        Assert.True(File.Exists(restored.ArtifactPointer));
    }

    [Fact]
    public void TryRestore_ArtifactPointerOutsideCheckDirectory_FallsBackToTargetDirectory()
    {
        using var workspace = GitWorkspace.Create();
        var runDirectory = Path.Combine(workspace.Root, ".distill", "runs", "d-test");
        var checkDirectory = Path.Combine(runDirectory, "build");
        Directory.CreateDirectory(checkDirectory);

        var check = BuildCheck();
        var context = new DistillRunContext
        {
            RunId = "d-test",
            WorkspaceRoot = workspace.Root,
            RunDirectory = runDirectory,
            Profile = "quick"
        };

        CheckResultCache.TryStore(
            check,
            context,
            new CheckRunResult(
                "build",
                "build",
                VerificationStatus.Pass,
                0,
                Array.Empty<DistillDiagnostic>(),
                "msbuild-binlog",
                Path.Combine(workspace.Root, "outside-check-directory.log"),
                TimeSpan.FromSeconds(2)));

        var restoreContext = new DistillRunContext
        {
            RunId = "d-test-2",
            WorkspaceRoot = workspace.Root,
            RunDirectory = Path.Combine(workspace.Root, ".distill", "runs", "d-test-2"),
            Profile = "quick"
        };

        Assert.True(CheckResultCache.TryRestore(check, restoreContext, out var restored));
        Assert.NotNull(restored);
        Assert.Equal(
            Path.Combine(restoreContext.RunDirectory, "build"),
            restored!.ArtifactPointer);
    }

    [Fact]
    public void TryRestore_TestCheckWithAutoSource_IsNeverCacheEligible()
    {
        using var workspace = GitWorkspace.Create();
        var runDirectory = Path.Combine(workspace.Root, ".distill", "runs", "d-test");
        var checkDirectory = Path.Combine(runDirectory, "unit");
        Directory.CreateDirectory(checkDirectory);
        File.WriteAllText(Path.Combine(checkDirectory, "stdout.log"), "cached output");

        var check = new PlannedCheck(
            "unit",
            new CheckConfig
            {
                Kind = "test",
                Command = "dotnet test App.sln",
                Source = "auto"
            },
            Array.Empty<string>());

        var context = new DistillRunContext
        {
            RunId = "d-test",
            WorkspaceRoot = workspace.Root,
            RunDirectory = runDirectory,
            Profile = "quick"
        };

        CheckResultCache.TryStore(
            check,
            context,
            new CheckRunResult(
                "unit",
                "test",
                VerificationStatus.Pass,
                0,
                Array.Empty<DistillDiagnostic>(),
                "vstest-composite",
                Path.Combine(checkDirectory, "stdout.log"),
                TimeSpan.FromSeconds(2)));

        var entryCount = Directory.Exists(Path.Combine(workspace.Root, ".distill", "cache"))
            ? Directory.EnumerateDirectories(Path.Combine(workspace.Root, ".distill", "cache")).Count()
            : 0;
        Assert.Equal(0, entryCount);

        var restoreContext = new DistillRunContext
        {
            RunId = "d-test-2",
            WorkspaceRoot = workspace.Root,
            RunDirectory = Path.Combine(workspace.Root, ".distill", "runs", "d-test-2"),
            Profile = "quick"
        };

        Assert.False(CheckResultCache.TryRestore(check, restoreContext, out var restored));
        Assert.Null(restored);
    }

    [Fact]
    public void TryRestore_TestCheckWithExplicitVstestSource_RemainsCacheEligible()
    {
        using var workspace = GitWorkspace.Create();
        var runDirectory = Path.Combine(workspace.Root, ".distill", "runs", "d-test");
        var checkDirectory = Path.Combine(runDirectory, "unit");
        Directory.CreateDirectory(checkDirectory);
        File.WriteAllText(Path.Combine(checkDirectory, "stdout.log"), "cached output");

        var check = new PlannedCheck(
            "unit",
            new CheckConfig
            {
                Kind = "test",
                Command = "dotnet test App.sln",
                Source = "vstest"
            },
            Array.Empty<string>());

        var context = new DistillRunContext
        {
            RunId = "d-test",
            WorkspaceRoot = workspace.Root,
            RunDirectory = runDirectory,
            Profile = "quick"
        };

        CheckResultCache.TryStore(
            check,
            context,
            new CheckRunResult(
                "unit",
                "test",
                VerificationStatus.Pass,
                0,
                Array.Empty<DistillDiagnostic>(),
                "vstest-composite",
                Path.Combine(checkDirectory, "stdout.log"),
                TimeSpan.FromSeconds(2)));

        var restoreContext = new DistillRunContext
        {
            RunId = "d-test-2",
            WorkspaceRoot = workspace.Root,
            RunDirectory = Path.Combine(workspace.Root, ".distill", "runs", "d-test-2"),
            Profile = "quick"
        };

        Assert.True(CheckResultCache.TryRestore(check, restoreContext, out var restored));
        Assert.NotNull(restored);
    }

    [Fact]
    public void TryRestore_InvalidDiagnosticEnum_ReturnsFalseWithoutThrowing()
    {
        using var workspace = GitWorkspace.Create();
        var check = BuildCheck();
        StorePassCache(workspace, check);
        OverwriteCachedMetadata(
            workspace.Root,
            """
            {
              "cacheKey": "invalid",
              "checkId": "build",
              "kind": "build",
              "status": "Pass",
              "exitCode": 0,
              "sourceId": "msbuild-binlog",
              "artifactPointer": "stdout.log",
              "durationMs": 1000,
              "diagnostics": [
                {
                  "id": "build:1",
                  "kind": "NotAKind",
                  "severity": "Error",
                  "source": "build",
                  "code": "CS0001",
                  "message": "failed",
                  "provenance": "RawFallback",
                  "confidence": 1.0
                }
              ]
            }
            """);

        Assert.False(TryRestoreWithoutException(check, workspace.Root, out var restored));
        Assert.Null(restored);
    }

    private static bool TryRestoreWithoutException(
        PlannedCheck check,
        string workspaceRoot,
        out CheckRunResult? restored)
    {
        restored = null;
        var context = new DistillRunContext
        {
            RunId = "d-test",
            WorkspaceRoot = workspaceRoot,
            RunDirectory = Path.Combine(workspaceRoot, ".distill", "runs", "d-test"),
            Profile = "quick"
        };

        CheckRunResult? captured = null;
        var success = false;
        var exception = Record.Exception(() =>
            success = CheckResultCache.TryRestore(check, context, out captured));
        Assert.Null(exception);
        restored = captured;
        return success;
    }

    [Fact]
    public async Task TryRestoreAsync_PreCanceledToken_ThrowsOperationCanceled()
    {
        using var workspace = GitWorkspace.Create();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var check = BuildCheck();
        var context = new DistillRunContext
        {
            RunId = "d-test",
            WorkspaceRoot = workspace.Root,
            RunDirectory = Path.Combine(workspace.Root, ".distill", "runs", "d-test"),
            Profile = "quick"
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CheckResultCache.TryRestoreAsync(check, context, cts.Token));
    }

    [Fact]
    public async Task TryStoreAsync_PreCanceledToken_ThrowsOperationCanceled()
    {
        using var workspace = GitWorkspace.Create();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var check = BuildCheck();
        var context = new DistillRunContext
        {
            RunId = "d-test",
            WorkspaceRoot = workspace.Root,
            RunDirectory = Path.Combine(workspace.Root, ".distill", "runs", "d-test"),
            Profile = "quick"
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CheckResultCache.TryStoreAsync(
                check,
                context,
                new CheckRunResult(
                    "build",
                    "build",
                    VerificationStatus.Pass,
                    0,
                    Array.Empty<DistillDiagnostic>(),
                    "msbuild-binlog",
                    null,
                    TimeSpan.FromSeconds(1)),
                cts.Token));
    }

    private static void StorePassCache(GitWorkspace workspace, PlannedCheck check)
    {
        var runDirectory = Path.Combine(workspace.Root, ".distill", "runs", "d-seed");
        var checkDirectory = Path.Combine(runDirectory, "build");
        Directory.CreateDirectory(checkDirectory);
        File.WriteAllText(Path.Combine(checkDirectory, "stdout.log"), "cached output");

        CheckResultCache.TryStore(
            check,
            new DistillRunContext
            {
                RunId = "d-seed",
                WorkspaceRoot = workspace.Root,
                RunDirectory = runDirectory,
                Profile = "quick"
            },
            new CheckRunResult(
                "build",
                "build",
                VerificationStatus.Pass,
                0,
                Array.Empty<DistillDiagnostic>(),
                "msbuild-binlog",
                Path.Combine(checkDirectory, "stdout.log"),
                TimeSpan.FromSeconds(1)));
    }

    private static string ReadCachedMetadataCacheKey(string workspaceRoot)
    {
        var metadataPath = Directory
            .EnumerateFiles(Path.Combine(workspaceRoot, ".distill", "cache"), "metadata.json", SearchOption.AllDirectories)
            .First();
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(metadataPath));
        return document.RootElement.GetProperty("cacheKey").GetString()!;
    }

    private static void OverwriteCachedMetadata(string workspaceRoot, string json)
    {
        var metadataPath = Directory
            .EnumerateFiles(Path.Combine(workspaceRoot, ".distill", "cache"), "metadata.json", SearchOption.AllDirectories)
            .First();
        File.WriteAllText(metadataPath, json);
    }

    private static PlannedCheck BuildCheck()
        => new(
            "build",
            new CheckConfig
            {
                Kind = "build",
                Command = "dotnet build App.sln",
                Source = "msbuild-binlog"
            },
            Array.Empty<string>());

    private sealed class CountingExecutor : ICheckExecutor
    {
        public int ExecutionCount { get; private set; }

        public Task<CheckRunResult> ExecuteAsync(
            PlannedCheck check,
            DistillRunContext context,
            CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return Task.FromResult(new CheckRunResult(
                check.Id,
                check.Definition.Kind,
                VerificationStatus.Pass,
                0,
                Array.Empty<DistillDiagnostic>(),
                "executed",
                null,
                TimeSpan.FromSeconds(1)));
        }
    }
}
