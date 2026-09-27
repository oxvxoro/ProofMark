using Distill.Git;
using Distill.Runner;

namespace Distill.Tests.Git;

public class ChangedFileCollectorTests
{
    [Fact]
    public async Task CollectAsync_NonGitWorkspace_ReturnsUnavailableSnapshot()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"distill-git-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        try
        {
            var collector = new ChangedFileCollector();
            var snapshot = await collector.CollectAsync(workspace, cancellationToken: CancellationToken.None);

            Assert.False(snapshot.IsAvailable);
            Assert.NotNull(snapshot.ErrorMessage);
            Assert.Empty(snapshot.ChangedFiles);
            Assert.Empty(snapshot.Hunks);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task CollectAsync_Cancellation_ThrowsOperationCanceled()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"distill-git-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var collector = new ChangedFileCollector();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            collector.CollectAsync(workspace, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task CollectAsync_WithBaseRevision_UsesDiffAgainstBase()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"distill-git-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        var statusPath = Path.Combine(workspace, "status.txt");
        var diffPath = Path.Combine(workspace, "diff.patch");
        try
        {
            var diffArguments = Array.Empty<string>();
            var nameOnlyArguments = Array.Empty<string>();
            var collector = new ChangedFileCollector(async (arguments, _, stdoutPath, _) =>
            {
                if (arguments.SequenceEqual(new[] { "status", "--porcelain" }))
                {
                    if (stdoutPath is not null)
                    {
                        await File.WriteAllTextAsync(stdoutPath, string.Empty);
                    }

                    return new ProcessResult(0, TimeSpan.Zero, ProcessStatus.Completed, stdoutPath, null);
                }

                if (arguments.SequenceEqual(new[] { "diff", "--find-renames", "--binary", "--unified=3", "main" }))
                {
                    diffArguments = arguments.ToArray();
                    if (stdoutPath is not null)
                    {
                        await File.WriteAllTextAsync(
                            stdoutPath,
                            """
                            diff --git a/src/Foo.cs b/src/Foo.cs
                            +++ b/src/Foo.cs
                            @@ -1,3 +1,4 @@
                            """);
                    }

                    return new ProcessResult(0, TimeSpan.Zero, ProcessStatus.Completed, stdoutPath, null);
                }

                if (arguments.SequenceEqual(new[] { "diff", "--name-only", "--find-renames", "main" }))
                {
                    nameOnlyArguments = arguments.ToArray();
                    if (stdoutPath is not null)
                    {
                        await File.WriteAllTextAsync(stdoutPath, "src/Foo.cs\n");
                    }

                    return new ProcessResult(0, TimeSpan.Zero, ProcessStatus.Completed, stdoutPath, null);
                }

                if (arguments.SequenceEqual(new[] { "ls-files", "-z", "--others", "--exclude-standard" }))
                {
                    if (stdoutPath is not null)
                    {
                        await File.WriteAllTextAsync(stdoutPath, "src/New.cs\0");
                    }

                    return new ProcessResult(0, TimeSpan.Zero, ProcessStatus.Completed, stdoutPath, null);
                }

                return new ProcessResult(1, TimeSpan.Zero, ProcessStatus.Completed, stdoutPath, null, "unexpected git command");
            });

            var snapshot = await collector.CollectAsync(
                workspace,
                statusPath,
                diffPath,
                baseRevision: "main",
                CancellationToken.None);

            Assert.True(snapshot.IsAvailable);
            Assert.Equal(["diff", "--find-renames", "--binary", "--unified=3", "main"], diffArguments);
            Assert.Equal(["diff", "--name-only", "--find-renames", "main"], nameOnlyArguments);
            Assert.Equal(["src/Foo.cs", "src/New.cs"], snapshot.ChangedFiles);
            Assert.Equal(["src/New.cs"], snapshot.UntrackedFiles);
            Assert.NotEmpty(snapshot.Hunks);
            Assert.Contains("src/Foo.cs", snapshot.DiffPatch);
            Assert.False(snapshot.WorkingTreeDirty);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task CollectAsync_StatusSucceedsButAllDiffsFail_ReturnsUnavailableSnapshot()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"distill-git-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        var statusPath = Path.Combine(workspace, "status.txt");
        var diffPath = Path.Combine(workspace, "diff.patch");
        try
        {
            var collector = new ChangedFileCollector(async (arguments, _, stdoutPath, _) =>
            {
                if (arguments.Contains("status"))
                {
                    if (stdoutPath is not null)
                    {
                        await File.WriteAllTextAsync(stdoutPath, string.Empty);
                    }

                    return new ProcessResult(0, TimeSpan.Zero, ProcessStatus.Completed, stdoutPath, null);
                }

                if (arguments.SequenceEqual(new[] { "diff", "--find-renames", "--binary", "--unified=3", "HEAD" }))
                {
                    return new ProcessResult(null, TimeSpan.Zero, ProcessStatus.TimedOut, stdoutPath, null, "Process timed out.");
                }

                return new ProcessResult(1, TimeSpan.Zero, ProcessStatus.Completed, stdoutPath, null, "Git diff command failed.");
            });

            var snapshot = await collector.CollectAsync(
                workspace,
                statusPath,
                diffPath,
                cancellationToken: CancellationToken.None);

            Assert.False(snapshot.IsAvailable);
            Assert.NotNull(snapshot.ErrorMessage);
            Assert.Empty(snapshot.ChangedFiles);
            Assert.Empty(snapshot.Hunks);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task CollectAsync_WithoutDiffPath_PreservesHunksFromOwnedTempFile()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"distill-git-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        try
        {
            var collector = new ChangedFileCollector(async (arguments, _, stdoutPath, _) =>
            {
                if (arguments.Contains("status"))
                {
                    if (stdoutPath is not null)
                    {
                        await File.WriteAllTextAsync(stdoutPath, " M src/Foo.cs\n");
                    }

                    return new ProcessResult(0, TimeSpan.Zero, ProcessStatus.Completed, stdoutPath, null);
                }

                if (arguments.Contains("diff") && arguments.Contains("--unified=3"))
                {
                    if (stdoutPath is not null)
                    {
                        await File.WriteAllTextAsync(
                            stdoutPath,
                            """
                            diff --git a/src/Foo.cs b/src/Foo.cs
                            +++ b/src/Foo.cs
                            @@ -1,3 +1,4 @@
                            """);
                    }

                    return new ProcessResult(0, TimeSpan.Zero, ProcessStatus.Completed, stdoutPath, null);
                }

                if (arguments.SequenceEqual(new[] { "ls-files", "-z", "--others", "--exclude-standard" }))
                {
                    if (stdoutPath is not null)
                    {
                        await File.WriteAllTextAsync(stdoutPath, string.Empty);
                    }

                    return new ProcessResult(0, TimeSpan.Zero, ProcessStatus.Completed, stdoutPath, null);
                }

                return new ProcessResult(1, TimeSpan.Zero, ProcessStatus.Completed, stdoutPath, null, "unexpected git command");
            });

            var snapshot = await collector.CollectAsync(workspace, cancellationToken: CancellationToken.None);

            Assert.True(snapshot.IsAvailable);
            Assert.NotEmpty(snapshot.Hunks);
            Assert.Contains("src/Foo.cs", snapshot.DiffPatch);
            Assert.Contains(snapshot.ChangedFiles, file => file.Replace('\\', '/') == "src/Foo.cs" || file.EndsWith("Foo.cs", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }
}
