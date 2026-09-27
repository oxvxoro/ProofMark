using Distill.Core.Planning;
using Distill.Core.Runs;
using Distill.Tests.TestSupport;

namespace Distill.Tests.Planning;

public class CheckExecutorCoordinatorTests
{
    [Fact]
    public async Task ExecuteDependencyAwareAsync_IndependentChecksCanOverlap()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var overlapDetected = false;
        var executor = new TrackingExecutor(async (check, _) =>
        {
            if (string.Equals(check.Id, "slow", StringComparison.OrdinalIgnoreCase))
            {
                await gate.Task.ConfigureAwait(false);
            }
            else if (string.Equals(check.Id, "fast", StringComparison.OrdinalIgnoreCase))
            {
                overlapDetected = true;
            }

            return PassResult(check);
        });

        var context = CreateContext();
        var checks = new[]
        {
            Planned("build", dependsOn: []),
            Planned("slow", dependsOn: ["build"]),
            Planned("fast", dependsOn: ["build"])
        };

        var runTask = CheckExecutorCoordinator.ExecuteDependencyAwareAsync(
            executor,
            checks,
            context,
            CancellationToken.None);

        await Task.Delay(100);
        gate.SetResult();
        var results = await runTask;

        Assert.True(overlapDetected);
        Assert.Equal(["build", "slow", "fast"], results.Select(result => result.CheckId));
    }

    [Fact]
    public async Task ExecuteDependencyAwareAsync_StopOnFailure_OmitsLaterChecks()
    {
        var executor = new TrackingExecutor((check, _) =>
        {
            if (string.Equals(check.Id, "unit", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new CheckRunResult(
                    check.Id,
                    check.Definition.Kind,
                    VerificationStatus.Fail,
                    1,
                    Array.Empty<Distill.Core.Diagnostics.DistillDiagnostic>(),
                    "generic",
                    null,
                    TimeSpan.FromSeconds(1)));
            }

            return Task.FromResult(PassResult(check));
        });

        var results = await CheckExecutorCoordinator.ExecuteSequentialAsync(
            executor,
            new[]
            {
                Planned("build", dependsOn: []),
                Planned("unit", dependsOn: ["build"], stopOnFailure: true),
                Planned("format", dependsOn: ["build"])
            },
            CreateContext(),
            CancellationToken.None);

        Assert.Equal(["build", "unit", "format"], results.Select(result => result.CheckId));
        var format = Assert.Single(results, result => result.CheckId == "format");
        Assert.Equal(VerificationStatus.Uncertain, format.Status);
        Assert.Equal("CHECK_NOT_SCHEDULED", format.Diagnostics[0].Code);
    }

    [Fact]
    public async Task ExecuteDependencyAwareAsync_FailedDependencyWithoutStopOnFailure_ChildDoesNotRun()
    {
        // Brief 02 / Assumption A2: dependsOn은 순서만이 아니라 성공 전제다.
        // Pass하지 않은 의존성은, 그 의존성의 stopOnFailure가 false여도
        // 의존 대상이 스케줄되지 못하게 막아야 한다.
        var executor = new TrackingExecutor((check, _) =>
        {
            if (string.Equals(check.Id, "build", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new CheckRunResult(
                    check.Id,
                    check.Definition.Kind,
                    VerificationStatus.Fail,
                    1,
                    Array.Empty<Distill.Core.Diagnostics.DistillDiagnostic>(),
                    "generic",
                    null,
                    TimeSpan.FromSeconds(1)));
            }

            return Task.FromResult(PassResult(check));
        });

        var results = await CheckExecutorCoordinator.ExecuteSequentialAsync(
            executor,
            new[]
            {
                Planned("build", dependsOn: [], stopOnFailure: false),
                Planned("unit", dependsOn: ["build"])
            },
            CreateContext(),
            CancellationToken.None);

        Assert.Equal(["build", "unit"], results.Select(result => result.CheckId));
        var unit = Assert.Single(results, result => result.CheckId == "unit");
        Assert.Equal(VerificationStatus.Uncertain, unit.Status);
        Assert.Equal("CHECK_BLOCKED", unit.Diagnostics[0].Code);
        Assert.Null(unit.ExitCode);
        Assert.Null(unit.ArtifactPointer);
        Assert.Equal(TimeSpan.Zero, unit.Duration);
    }

    [Fact]
    public async Task ExecuteDependencyAwareAsync_FailedRoot_BlocksNestedDescendantAsCheckBlocked()
    {
        var executed = new List<string>();
        var executor = new TrackingExecutor((check, _) =>
        {
            executed.Add(check.Id);
            if (string.Equals(check.Id, "a", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new CheckRunResult(
                    check.Id,
                    check.Definition.Kind,
                    VerificationStatus.Fail,
                    1,
                    Array.Empty<Distill.Core.Diagnostics.DistillDiagnostic>(),
                    "generic",
                    null,
                    TimeSpan.FromSeconds(1)));
            }

            return Task.FromResult(PassResult(check));
        });

        var results = await CheckExecutorCoordinator.ExecuteSequentialAsync(
            executor,
            new[]
            {
                Planned("a", dependsOn: [], stopOnFailure: false),
                Planned("b", dependsOn: ["a"]),
                Planned("c", dependsOn: ["b"])
            },
            CreateContext(),
            CancellationToken.None);

        Assert.Equal(["a"], executed);
        Assert.Equal(["a", "b", "c"], results.Select(result => result.CheckId));
        Assert.All(
            results.Where(result => result.CheckId is "b" or "c"),
            result =>
            {
                Assert.Equal(VerificationStatus.Uncertain, result.Status);
                Assert.Equal("CHECK_BLOCKED", result.Diagnostics[0].Code);
            });
    }

    [Fact]
    public async Task ExecuteDependencyAwareAsync_UncertainDependencyWithoutStopOnFailure_ChildDoesNotRun()
    {
        var executor = new TrackingExecutor((check, _) =>
        {
            if (string.Equals(check.Id, "build", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new CheckRunResult(
                    check.Id,
                    check.Definition.Kind,
                    VerificationStatus.Uncertain,
                    1,
                    Array.Empty<Distill.Core.Diagnostics.DistillDiagnostic>(),
                    "generic",
                    null,
                    TimeSpan.FromSeconds(1)));
            }

            return Task.FromResult(PassResult(check));
        });

        var results = await CheckExecutorCoordinator.ExecuteSequentialAsync(
            executor,
            new[]
            {
                Planned("build", dependsOn: [], stopOnFailure: false),
                Planned("unit", dependsOn: ["build"])
            },
            CreateContext(),
            CancellationToken.None);

        Assert.Equal(["build", "unit"], results.Select(result => result.CheckId));
        var unit = Assert.Single(results, result => result.CheckId == "unit");
        Assert.Equal(VerificationStatus.Uncertain, unit.Status);
        Assert.Equal("CHECK_BLOCKED", unit.Diagnostics[0].Code);
    }

    [Fact]
    public async Task ExecuteDependencyAwareAsync_PassedDependency_ChildRuns()
    {
        var executor = new TrackingExecutor((check, _) => Task.FromResult(PassResult(check)));

        var results = await CheckExecutorCoordinator.ExecuteSequentialAsync(
            executor,
            new[]
            {
                Planned("build", dependsOn: [], stopOnFailure: false),
                Planned("unit", dependsOn: ["build"])
            },
            CreateContext(),
            CancellationToken.None);

        Assert.Equal(["build", "unit"], results.Select(result => result.CheckId));
    }

    [Fact]
    public async Task ExecuteDependencyAwareAsync_StopOnFailure_AllowsInFlightSiblingToFinishBeforeStoppingNewWork()
    {
        var bGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = new TrackingExecutor(async (check, _) =>
        {
            if (string.Equals(check.Id, "a", StringComparison.OrdinalIgnoreCase))
            {
                return new CheckRunResult(
                    check.Id,
                    check.Definition.Kind,
                    VerificationStatus.Fail,
                    1,
                    Array.Empty<Distill.Core.Diagnostics.DistillDiagnostic>(),
                    "generic",
                    null,
                    TimeSpan.FromSeconds(1));
            }

            if (string.Equals(check.Id, "b", StringComparison.OrdinalIgnoreCase))
            {
                await bGate.Task.ConfigureAwait(false);
            }

            return PassResult(check);
        });

        var context = CreateContext();
        var checks = new[]
        {
            Planned("root", dependsOn: []),
            Planned("a", dependsOn: ["root"], stopOnFailure: true),
            Planned("b", dependsOn: ["root"]),
            Planned("c", dependsOn: ["b"])
        };

        var runTask = CheckExecutorCoordinator.ExecuteDependencyAwareAsync(
            executor,
            checks,
            context,
            CancellationToken.None);

        await Task.Delay(150);
        bGate.SetResult();
        var results = await runTask;

        Assert.Equal(["root", "a", "b", "c"], results.Select(result => result.CheckId));
        var c = Assert.Single(results, result => result.CheckId == "c");
        Assert.Equal(VerificationStatus.Uncertain, c.Status);
        Assert.Equal("CHECK_NOT_SCHEDULED", c.Diagnostics[0].Code);
    }

    [Fact]
    public async Task ExecuteDependencyAwareAsync_UncertainResult_DoesNotStopScheduling()
    {
        var executor = new TrackingExecutor((check, _) =>
        {
            if (string.Equals(check.Id, "unit", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new CheckRunResult(
                    check.Id,
                    check.Definition.Kind,
                    VerificationStatus.Uncertain,
                    1,
                    Array.Empty<Distill.Core.Diagnostics.DistillDiagnostic>(),
                    "generic",
                    null,
                    TimeSpan.FromSeconds(1)));
            }

            return Task.FromResult(PassResult(check));
        });

        var results = await CheckExecutorCoordinator.ExecuteSequentialAsync(
            executor,
            new[]
            {
                Planned("build", dependsOn: []),
                Planned("unit", dependsOn: ["build"], stopOnFailure: true),
                Planned("format", dependsOn: ["build"])
            },
            CreateContext(),
            CancellationToken.None);

        Assert.Equal(["build", "unit", "format"], results.Select(result => result.CheckId));
    }

    [Fact]
    public async Task ExecuteDependencyAwareAsync_InfraErrorWithStopOnFailure_StopsScheduling()
    {
        var executor = new TrackingExecutor((check, _) =>
        {
            if (string.Equals(check.Id, "unit", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new CheckRunResult(
                    check.Id,
                    check.Definition.Kind,
                    VerificationStatus.InfraError,
                    null,
                    Array.Empty<Distill.Core.Diagnostics.DistillDiagnostic>(),
                    "generic",
                    null,
                    TimeSpan.FromSeconds(1)));
            }

            return Task.FromResult(PassResult(check));
        });

        var results = await CheckExecutorCoordinator.ExecuteSequentialAsync(
            executor,
            new[]
            {
                Planned("build", dependsOn: []),
                Planned("unit", dependsOn: ["build"], stopOnFailure: true),
                Planned("format", dependsOn: ["build"])
            },
            CreateContext(),
            CancellationToken.None);

        Assert.Equal(["build", "unit", "format"], results.Select(result => result.CheckId));
        var format = Assert.Single(results, result => result.CheckId == "format");
        Assert.Equal(VerificationStatus.Uncertain, format.Status);
        Assert.Equal("CHECK_NOT_SCHEDULED", format.Diagnostics[0].Code);
    }

    [Fact]
    public async Task ExecuteDependencyAwareAsync_MaxParallelismOne_BehavesLikeSequential()
    {
        var order = new List<string>();
        var executor = new TrackingExecutor((check, _) =>
        {
            order.Add(check.Id);
            return Task.FromResult(PassResult(check));
        });

        var checks = new[]
        {
            Planned("build", dependsOn: []),
            Planned("unit", dependsOn: ["build"]),
            Planned("format", dependsOn: ["build"])
        };

        var results = await CheckExecutorCoordinator.ExecuteDependencyAwareAsync(
            executor,
            checks,
            CreateContext(),
            CancellationToken.None,
            maxParallelism: 1);

        Assert.Equal(["build", "unit", "format"], results.Select(result => result.CheckId));
        Assert.Equal(3, order.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ExecuteDependencyAwareAsync_InvalidMaxParallelism_ThrowsArgumentOutOfRange(int maxParallelism)
    {
        var executor = new TrackingExecutor((check, _) => Task.FromResult(PassResult(check)));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            CheckExecutorCoordinator.ExecuteDependencyAwareAsync(
                executor,
                new[] { Planned("build", dependsOn: []) },
                CreateContext(),
                CancellationToken.None,
                maxParallelism));
    }

    [Fact]
    public async Task ExecuteDependencyAwareAsync_CacheHitParent_UnblocksDependentChild()
    {
        using var workspace = GitWorkspace.Create();
        var context = new DistillRunContext
        {
            RunId = "d-cache-parent",
            WorkspaceRoot = workspace.Root,
            RunDirectory = Path.Combine(workspace.Root, ".distill", "runs", "d-cache-parent"),
            Profile = "quick"
        };

        var buildPlanned = Planned("build", dependsOn: []);
        var priorRunContext = new DistillRunContext
        {
            RunId = "d-cache-seed",
            WorkspaceRoot = workspace.Root,
            RunDirectory = Path.Combine(workspace.Root, ".distill", "runs", "d-cache-seed"),
            Profile = "quick"
        };
        Directory.CreateDirectory(Path.Combine(priorRunContext.RunDirectory, "build"));
        CheckResultCache.TryStore(buildPlanned, priorRunContext, PassResult(buildPlanned));

        var executedIds = new List<string>();
        var executor = new TrackingExecutor((check, _) =>
        {
            executedIds.Add(check.Id);
            return Task.FromResult(PassResult(check));
        });

        var checks = new[]
        {
            buildPlanned,
            Planned("unit", dependsOn: ["build"])
        };

        var results = await CheckExecutorCoordinator.ExecuteDependencyAwareAsync(
            executor,
            checks,
            context,
            CancellationToken.None);

        Assert.Equal(["build", "unit"], results.Select(result => result.CheckId));
        Assert.DoesNotContain("build", executedIds);
        Assert.Contains("unit", executedIds);
    }

    [Fact]
    public async Task ExecuteDependencyAwareAsync_ExecutorThrowsOperationCanceled_PropagatesToCaller()
    {
        var executor = new TrackingExecutor((_, _) => throw new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CheckExecutorCoordinator.ExecuteDependencyAwareAsync(
                executor,
                new[] { Planned("build", dependsOn: []) },
                CreateContext(),
                CancellationToken.None));
    }

    [Fact]
    public void ResolveOverallStatus_FailAndUncertain_ReturnsFail()
    {
        // Brief 01 / Assumption A1: 확정된 Fail은 다른
        // 검사의 Uncertain 결과에 가려지면 안 된다. 우선순위는 InfraError > Fail > Uncertain > Pass다.
        var results = new[]
        {
            new CheckRunResult(
                "build",
                "build",
                VerificationStatus.Fail,
                1,
                Array.Empty<Distill.Core.Diagnostics.DistillDiagnostic>(),
                "generic",
                null,
                TimeSpan.FromSeconds(1)),
            new CheckRunResult(
                "unit",
                "test",
                VerificationStatus.Uncertain,
                1,
                Array.Empty<Distill.Core.Diagnostics.DistillDiagnostic>(),
                "generic",
                null,
                TimeSpan.FromSeconds(1))
        };

        Assert.Equal(VerificationStatus.Fail, CheckExecutorCoordinator.ResolveOverallStatus(results));
    }

    [Fact]
    public void ResolveOverallStatus_InfraErrorAndFail_ReturnsInfraError()
    {
        var results = new[]
        {
            new CheckRunResult(
                "build",
                "build",
                VerificationStatus.Fail,
                1,
                Array.Empty<Distill.Core.Diagnostics.DistillDiagnostic>(),
                "generic",
                null,
                TimeSpan.FromSeconds(1)),
            new CheckRunResult(
                "unit",
                "test",
                VerificationStatus.InfraError,
                null,
                Array.Empty<Distill.Core.Diagnostics.DistillDiagnostic>(),
                "generic",
                null,
                TimeSpan.FromSeconds(1))
        };

        Assert.Equal(VerificationStatus.InfraError, CheckExecutorCoordinator.ResolveOverallStatus(results));
    }

    [Fact]
    public void ResolveOverallStatus_UncertainAndPass_ReturnsUncertain()
    {
        var results = new[]
        {
            new CheckRunResult(
                "build",
                "build",
                VerificationStatus.Pass,
                0,
                Array.Empty<Distill.Core.Diagnostics.DistillDiagnostic>(),
                "generic",
                null,
                TimeSpan.FromSeconds(1)),
            new CheckRunResult(
                "unit",
                "test",
                VerificationStatus.Uncertain,
                1,
                Array.Empty<Distill.Core.Diagnostics.DistillDiagnostic>(),
                "generic",
                null,
                TimeSpan.FromSeconds(1))
        };

        Assert.Equal(VerificationStatus.Uncertain, CheckExecutorCoordinator.ResolveOverallStatus(results));
    }

    private static DistillRunContext CreateContext()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"distill-coordinator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        return new DistillRunContext
        {
            RunId = "test-run",
            WorkspaceRoot = workspace,
            RunDirectory = Path.Combine(workspace, ".distill", "runs", "test-run"),
            Profile = "quick"
        };
    }

    private static PlannedCheck Planned(string id, IReadOnlyList<string> dependsOn, bool stopOnFailure = false)
        => new(
            id,
            new Distill.Core.Config.CheckConfig
            {
                Kind = id,
                Command = $"dotnet {id}",
                DependsOn = dependsOn.ToList(),
                StopOnFailure = stopOnFailure
            },
            dependsOn);

    private static CheckRunResult PassResult(PlannedCheck check)
        => new(
            check.Id,
            check.Definition.Kind,
            VerificationStatus.Pass,
            0,
            Array.Empty<Distill.Core.Diagnostics.DistillDiagnostic>(),
            "generic",
            null,
            TimeSpan.FromSeconds(1));

    private sealed class TrackingExecutor(Func<PlannedCheck, DistillRunContext, Task<CheckRunResult>> execute) : ICheckExecutor
    {
        public Task<CheckRunResult> ExecuteAsync(
            PlannedCheck check,
            DistillRunContext context,
            CancellationToken cancellationToken)
            => execute(check, context);
    }
}
