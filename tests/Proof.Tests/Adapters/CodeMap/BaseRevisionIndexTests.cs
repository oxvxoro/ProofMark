using CodeMap.Core.Models;
using Proof.Adapters.CodeMap;
using Proof.Core;
using System.Diagnostics;

namespace Proof.Tests;

/// <summary>
/// 옵트인 base-revision 삭제 경로의 통합 테스트. 테스트가 임시 git
/// 저장소를 만든다(절대 남기면 안 된다. worktree 정리는 분석기의 finally 블록에서,
/// 저장소 정리는 테스트에서 실행된다).
/// </summary>
public sealed class BaseRevisionIndexTests
{
    [Fact(Timeout = 120_000)]
    public async Task AnalyzeDeletionImpactAsync_ResolvesCallersFromBaseRevisionWorktree()
    {
        var repo = CreateRepository();
        try
        {
            var headSha = RunGit(repo, "rev-parse HEAD").Trim();
            File.Delete(Path.Combine(repo, "App", "BillingService.cs"));

            var result = await BaseRevisionIndex.AnalyzeDeletionImpactAsync(
                repo,
                headSha,
                ["App/BillingService.cs"],
                new ResolvedImpactBudget(2, 500, 50, 50, 0.75),
                CancellationToken.None);

            Assert.NotNull(result);
            Assert.Contains(result.ResolvedPaths, path => path.EndsWith("BillingService.cs", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.DeletedSymbols, symbol => symbol.DisplayName.Contains("BillingService", StringComparison.Ordinal));
            var callerRelation = Assert.Single(result.CallerRelations, relation =>
                string.Equals(relation.CallerProject, "App", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("OrderService", callerRelation.CallerSymbolId, StringComparison.Ordinal);

            // 두 번째 실행은 SHA별 캐시된 base 인덱스를 재사용하고
            // 같은 결과를 반환해야 한다.
            var cached = await BaseRevisionIndex.AnalyzeDeletionImpactAsync(
                repo,
                headSha,
                ["App/BillingService.cs"],
                new ResolvedImpactBudget(2, 500, 50, 50, 0.75),
                CancellationToken.None);
            Assert.NotNull(cached);
            Assert.Equal(result.ResolvedPaths, cached.ResolvedPaths);
            Assert.True(Directory.Exists(Path.Combine(repo, ".proof", "base-index", headSha, ".codemap")));
        }
        finally
        {
            Cleanup(repo);
        }
    }

    private static string CreateRepository()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-baserev-test-" + Guid.NewGuid().ToString("N"));
        var source = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "fixtures", "proof", "simple-service"));
        Directory.CreateDirectory(root);
        foreach (var file in Directory.EnumerateFiles(source, "*.*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }

        File.WriteAllText(Path.Combine(root, ".gitignore"), ".codemap/\nbin/\nobj/\n");
        RunGit(root, "init");
        RunGit(root, "config user.email test@example.com");
        RunGit(root, "config user.name Test");
        RunGit(root, "add .");
        RunGit(root, "commit -m init");
        return root;
    }

    private static string RunGit(string workingDirectory, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Failed to start git.");

        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {arguments} failed: {process.StandardError.ReadToEnd()}");
        }

        return process.StandardOutput.ReadToEnd();
    }

    private static void Cleanup(string root)
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
