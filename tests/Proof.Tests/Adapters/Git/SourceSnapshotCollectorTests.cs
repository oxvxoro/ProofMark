using Distill.Git;
using Proof.Adapters.Git;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

public sealed class SourceSnapshotCollectorTests
{
    [Fact]
    public void FromGitSnapshot_IncludesUntrackedFiles()
    {
        var gitSnapshot = new GitChangeSnapshot(
            string.Empty,
            ["src/Foo.cs"],
            [],
            string.Empty,
            UntrackedFiles: ["src/New.cs"]);

        var snapshot = GitChangeMapper.ToSnapshot("root", "base", "head", gitSnapshot);

        Assert.True(snapshot.UsedFileWideFallback);
        Assert.Contains(snapshot.Files, file => file.NewPath == "src/New.cs");
        Assert.False(snapshot.ChangeSetIsEmpty);
        Assert.True(snapshot.ContentHashCaptureFailed);
    }

    [Fact]
    public void FromGitSnapshot_UsesWorkingTreeDirtyState()
    {
        var snapshot = GitChangeMapper.ToSnapshot(
            "root",
            "base",
            "head",
            new GitChangeSnapshot(
                string.Empty,
                ["src/Foo.cs"],
                [],
                string.Empty,
                WorkingTreeDirty: false));

        Assert.False(snapshot.IsDirty);
    }

    [Fact]
    public void MissingWorkspaceFile_IsContentHashCaptureFailure()
    {
        var snapshot = GitChangeMapper.ToSnapshot("missing-root", "base", "head", new GitChangeSnapshot(
            string.Empty,
            ["src/Gone.cs"],
            [],
            string.Empty,
            FileChanges:
            [
                new GitFileChange(GitFileChangeKind.Modified, "src/Gone.cs", "src/Gone.cs")
            ]));
        Assert.True(snapshot.ContentHashCaptureFailed);
        var plan = new DeterministicProofPlanner().Plan(new ChangeImpact(
            "base",
            "head",
            [],
            [],
            [],
            [],
            [],
            "complete",
            false,
            FileDeltas: snapshot.Files,
            ContentHashCaptureFailed: snapshot.ContentHashCaptureFailed));
        Assert.Contains(plan.Constraints ?? [], item => item.Code == ProofReasonCodes.ChangeCaptureContentHashFailed);
    }

    [Fact]
    public async Task CollectAsync_HashesOldContentViaBatchQuery()
    {
        var repo = CreateRepository();
        try
        {
            var headSha = RunGit(repo, "rev-parse HEAD").Trim();
            File.WriteAllText(Path.Combine(repo, "src", "a.txt"), "two");
            RunGit(repo, "add .");
            RunGit(repo, "commit -m change");

            // 워킹 트리에서 추적 파일을 삭제해, 스냅샷에 옛 blob을
            // base에서 해시해야 하는 Deleted 델타가 생기게 한다.
            File.Delete(Path.Combine(repo, "src", "a.txt"));

            var snapshot = await new GitSourceSnapshotCollector().CollectAsync(
                repo, headSha, "WORKTREE", CancellationToken.None);

            var expected = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("content-one"))).ToLowerInvariant();
            Assert.False(snapshot.ContentHashCaptureFailed);
            Assert.Contains(snapshot.Files, file => file.OldContentSha256 == expected);
        }
        finally
        {
            CleanupDirectory(repo);
        }
    }

    [Fact]
    public async Task CollectAsync_HashesManyOldBlobs_LargerThanPipeBuffer()
    {
        var repo = CreateRepository();
        try
        {
            // 수정된 파일 400개는 `git cat-file --batch`의 stdin과
            // stdout을 작은(Windows) 익명 파이프 버퍼 너머로 밀어 넣는다.
            // stdout을 비우기 전에 모든 경로를 쓰는 구현은
            // 여기서 교착되므로, 워치독이 멈춤을 실패로 바꾼다.
            for (var i = 0; i < 200; i++)
            {
                File.WriteAllText(Path.Combine(repo, "src", $"bulk-{i:D4}.txt"), new string('x', 256));
            }

            RunGit(repo, "add .");
            RunGit(repo, "commit -m bulk");
            var baseSha = RunGit(repo, "rev-parse HEAD").Trim();

            for (var i = 0; i < 200; i++)
            {
                File.WriteAllText(Path.Combine(repo, "src", $"bulk-{i:D4}.txt"), new string('y', 256));
            }

            var snapshot = await new GitSourceSnapshotCollector()
                .CollectAsync(repo, baseSha, "WORKTREE", CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(120));

            Assert.False(snapshot.ContentHashCaptureFailed);
            Assert.Equal(200, snapshot.Files.Count(file => file.OldContentSha256 is not null));
        }
        finally
        {
            CleanupDirectory(repo);
        }
    }

    private static string CreateRepository()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-snapshot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "src"));
        File.WriteAllText(Path.Combine(root, "src", "a.txt"), "content-one");
        RunGit(root, "init");
        RunGit(root, "config user.email test@example.com");
        RunGit(root, "config user.name Test");
        RunGit(root, "config core.autocrlf false");
        RunGit(root, "add .");
        RunGit(root, "commit -m init");
        return root;
    }

    private static string RunGit(string workingDirectory, string arguments)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Failed to start git.");

        // git이 도는 동안 양쪽 파이프를 비운다. 큰 배치(예: 수백 개 파일의 git add)는
        // 작은 Windows 파이프 버퍼보다 많은 양을 출력할 수 있다.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdoutTask, stderrTask);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {arguments} failed: {stderrTask.Result}");
        }

        return stdoutTask.Result;
    }

    private static void CleanupDirectory(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public sealed class CaptureConstraintTests
{
    [Fact]
    public void StructuredDeltaFailed_IsBlocking()
    {
        var plan = new DeterministicProofPlanner().Plan(new ChangeImpact(
            "base",
            "head",
            [new LineSpan("src/A.cs", 1, 2)],
            [],
            [],
            [],
            [],
            "complete",
            false,
            StructuredDeltaFailed: true));
        Assert.Contains(plan.Constraints ?? [], item => item.Code == ProofReasonCodes.ChangeStructuredDeltaFailed);
    }

    [Fact]
    public void UnsupportedSymlinkDelta_IsBlocking()
    {
        var plan = new DeterministicProofPlanner().Plan(new ChangeImpact(
            "base",
            "head",
            [],
            [],
            [],
            [],
            [],
            "complete",
            false,
            FileDeltas:
            [
                new FileDelta(FileChangeKind.Unsupported, "link", "link", [], [])
            ]));
        Assert.Contains(plan.Constraints ?? [], item => item.Code == ProofReasonCodes.ChangeUnsupportedKind);
    }

    [Fact]
    public void ToChangeRequest_ProjectsFileDeltasToSpans()
    {
        var snapshot = new SourceSnapshot(
            "root",
            "base",
            "head",
            IsDirty: true,
            SourceDigest: "digest",
            [
                new FileDelta(
                    FileChangeKind.Modified,
                    "src/Foo.cs",
                    "src/Foo.cs",
                    [],
                    [new LineSpan("src/Foo.cs", 10, 20)])
            ],
            ChangeSetIsEmpty: false);

        var request = SourceSnapshotCollector.ToChangeRequest(snapshot);

        Assert.Single(request.Spans);
        Assert.Equal("src/Foo.cs", request.Spans[0].File);
        Assert.Equal(10, request.Spans[0].StartLine);
    }

    [Fact]
    public void CreateChangeRequest_DelegatesToCollector()
    {
        var gitSnapshot = new GitChangeSnapshot(
            string.Empty,
            ["src/Foo.cs"],
            [],
            string.Empty,
            UntrackedFiles: ["src/New.cs"]);

        var snapshot = GitChangeMapper.ToSnapshot("root", "base", "head", gitSnapshot);
        var request = SourceSnapshotCollector.ToChangeRequest(snapshot);
        var usedFallback = snapshot.UsedFileWideFallback;

        Assert.True(usedFallback);
        Assert.Contains(request.Spans, span => span.File == "src/New.cs");
    }

    [Fact]
    public void SourceDigest_MutatesWhenUntrackedContentMutates()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-hash-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(root, "src"));
        var path = Path.Combine(root, "src", "New.cs");
        try
        {
            File.WriteAllText(path, "class A {}");
            var first = GitChangeMapper.ToSnapshot(root, "base", "head", new GitChangeSnapshot(
                string.Empty, [], [], string.Empty, UntrackedFiles: ["src/New.cs"]));
            File.WriteAllText(path, "class B {}");
            var second = GitChangeMapper.ToSnapshot(root, "base", "head", new GitChangeSnapshot(
                string.Empty, [], [], string.Empty, UntrackedFiles: ["src/New.cs"]));
            Assert.NotEqual(first.SourceDigest, second.SourceDigest);
            Assert.False(first.ContentHashCaptureFailed);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SourceDigest_IsInvariantToFilePermutation()
    {
        var a = Delta(FileChangeKind.Modified, "src/A.cs", "src/B.cs");
        var b = Delta(FileChangeKind.Added, null, "src/C.cs");
        var first = Snapshot([a, b]);
        var second = Snapshot([b, a]);
        Assert.Equal(SourceSnapshotHasher.Compute(first), SourceSnapshotHasher.Compute(second));
    }

    [Fact]
    public void SourceDigest_NormalizesSlashDirection()
    {
        var backslash = Snapshot([Delta(FileChangeKind.Modified, @"src\Foo.cs", @"src\Foo.cs")]);
        var slash = Snapshot([Delta(FileChangeKind.Modified, "src/Foo.cs", "src/Foo.cs")]);
        Assert.Equal(SourceSnapshotHasher.Compute(backslash), SourceSnapshotHasher.Compute(slash));
    }

    [Fact]
    public void SourceDigest_MutatesWhenKindOrRenameChanges()
    {
        var modified = Snapshot([Delta(FileChangeKind.Modified, "src/Foo.cs", "src/Foo.cs")]);
        var renamed = Snapshot([Delta(FileChangeKind.Renamed, "src/Foo.cs", "src/Bar.cs")]);
        Assert.NotEqual(SourceSnapshotHasher.Compute(modified), SourceSnapshotHasher.Compute(renamed));
    }

    [Fact]
    public void SourceDigest_EmptyCleanWorkspaceIsStable()
    {
        var first = new SourceSnapshot("root", "base", "head", false, "unused", [], true);
        var second = new SourceSnapshot("root", "base", "head", false, "unused", [], true);
        Assert.Equal(SourceSnapshotHasher.Compute(first), SourceSnapshotHasher.Compute(second));
        Assert.False(string.IsNullOrWhiteSpace(SourceSnapshotHasher.Compute(first)));
    }

    [Fact]
    public void UntrackedSymlink_IsUnsupported()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-link-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "target.txt");
        var link = Path.Combine(root, "link.txt");
        File.WriteAllText(target, "payload");
        try
        {
            File.CreateSymbolicLink(link, target);
            var snapshot = GitChangeMapper.ToSnapshot(root, "base", "head", new GitChangeSnapshot(
                string.Empty, [], [], string.Empty, UntrackedFiles: ["link.txt"]));
            Assert.Contains(snapshot.Files, file =>
                file.Kind == FileChangeKind.Unsupported && file.NewPath == "link.txt");
            Assert.False(snapshot.ContentHashCaptureFailed);
        }
        catch (IOException) when (!File.Exists(link))
        {
            // 심볼릭 링크를 만들려면 일부 Windows 호스트에서 권한이 필요하다.
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BatchHashFailure_MarksContentHashCaptureFailed()
    {
        var original = SourceSnapshotCollector.Create(
            "root",
            "base",
            "head",
            [new FileDelta(FileChangeKind.Modified, "src/Foo.cs", "src/Foo.cs", [], [])],
            untrackedCaptureFailed: false,
            usedFileWideFallback: false,
            isDirty: false);
        Assert.False(original.ContentHashCaptureFailed);

        var failed = GitSourceSnapshotCollector.RebuildWithHashFailure(original);

        Assert.True(failed.ContentHashCaptureFailed);
        var request = SourceSnapshotCollector.ToChangeRequest(failed);
        var plan = new DeterministicProofPlanner().Plan(new ChangeImpact(
            request.BaseRevision,
            request.HeadRevision,
            request.Spans,
            [],
            [],
            [],
            [],
            "complete",
            false,
            FileDeltas: request.FileDeltas,
            ContentHashCaptureFailed: request.ContentHashCaptureFailed));
        Assert.Contains(plan.Constraints ?? [], item =>
            item.Code == ProofReasonCodes.ChangeCaptureContentHashFailed);
    }

    private static SourceSnapshot Snapshot(IReadOnlyList<FileDelta> files)
        => new("root", "base", "head", true, "unused", files, false);

    private static FileDelta Delta(FileChangeKind kind, string? oldPath, string? newPath)
        => new(kind, oldPath, newPath, [], []);
}
