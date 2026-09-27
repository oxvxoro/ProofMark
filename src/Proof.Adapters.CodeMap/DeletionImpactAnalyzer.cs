using System.Diagnostics;
using CodeMap.Core.Contracts;
using CodeMap.Core.Models;
using CodeMap.Engine.Indexing;
using CodeMap.Storage;
using CodeMap.Storage.Queries;
using Proof.Core;

namespace Proof.Adapters.CodeMap;

/// <summary>
/// 삭제된 파일을 아직 담고 있는 그래프 인덱스(갱신 전 인덱스, 또는 base
/// 리비전으로 만든 인덱스)에서 삭제 영향을 복원한다.
/// 삭제된 파일은 head 인덱스에 절대 나타나지 않으므로, 호출자는 base 그래프에서만
/// 정직하게 추적할 수 있다. 분석기는 휴리스틱 간선을 의미적 사실로 승격하지 않는다.
/// base 그래프의 호출자 관계를 head 영향에 병합하고,
/// 실제로 해석한 경로를 보고한다.
/// </summary>
internal sealed record DeletionImpactResult(
    IReadOnlyList<string> ResolvedPaths,
    IReadOnlyList<ChangedSymbolRef> DeletedSymbols,
    IReadOnlyList<CallerRelation> CallerRelations,
    IReadOnlyList<ImpactedSymbolRef> ImpactedSymbols,
    bool ImpactPotentiallyTruncated = false,
    bool CallerPotentiallyTruncated = false);

internal static class DeletionImpactAnalyzer
{
    /// <summary>파일 델타에서 삭제/이름 변경의 이전 경로를 모아 '/'로 정규화한다.</summary>
    public static IReadOnlyList<string> CollectDeletedPaths(IReadOnlyList<FileDelta>? fileDeltas)
    {
        if (fileDeltas is not { Count: > 0 })
        {
            return [];
        }

        var paths = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var delta in fileDeltas)
        {
            // 이름 변경은 head 그래프에서 NewPath를 살려 둔다. OldPath만
            // 사라지므로, 삭제와 같이 추적한다.
            if (delta.Kind is FileChangeKind.Deleted or FileChangeKind.Renamed
                && !string.IsNullOrWhiteSpace(delta.OldPath))
            {
                paths.Add(delta.OldPath.Replace('\\', '/'));
            }
        }

        return paths.ToArray();
    }

    /// <summary>
    /// 주어진 리더(base 그래프)에서 삭제된 경로의 심볼과 그 호출자를
    /// head 영향 순회와 같은 예산으로 조회한다.
    /// 리더가 어떤 삭제 경로의 심볼도 없으면 null을 반환한다.
    /// </summary>
    public static DeletionImpactResult? Analyze(
        ICodeMapGraphReader reader,
        IReadOnlyList<string> deletedPaths,
        ResolvedImpactBudget budget,
        IReadOnlyCollection<string> headChangedSymbolIds,
        Func<IndexedSymbol, bool>? isTestSymbol = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        isTestSymbol ??= TestProjectClassifier.IsTestSymbolHeuristic;

        if (deletedPaths.Count == 0)
        {
            return null;
        }

        var headIds = headChangedSymbolIds.ToHashSet(StringComparer.Ordinal);

        // 예산보다 심볼을 하나 더 가져와 한도 초과가 보이게 한다.
        // SymbolsInFiles는 이미 File/Namespace를 제외하므로, 탐색 개수는
        // 걸러진 예산과 직접 비교할 수 있다.
        var symbolProbeLimit = budget.MaxResults == int.MaxValue
            ? int.MaxValue
            : budget.MaxResults + 1;
        var filteredSymbols = reader
            .SymbolsInFiles(deletedPaths, symbolProbeLimit)
            .Where(symbol => symbol.Kind is not (NodeKind.File or NodeKind.Namespace))
            .OrderBy(symbol => symbol.QualifiedName, StringComparer.Ordinal)
            .ThenBy(symbol => symbol.Id, StringComparer.Ordinal)
            .ToArray();

        // 삭제 심볼 조회 절단은 보수적이다. 예산이 허용하는 것보다 심볼이
        // 더 있으면, 호출자 순회는 완전할 수 없다.
        var impactPotentiallyTruncated = filteredSymbols.Length > budget.MaxResults;
        var deletedSymbols = filteredSymbols
            .Take(budget.MaxResults)
            .ToArray();

        var resolvedPaths = new SortedSet<string>(StringComparer.Ordinal);
        var changedRefs = new List<ChangedSymbolRef>();
        var callers = new List<CallerRelation>();
        var impacted = new List<ImpactedSymbolRef>();
        var callerPotentiallyTruncated = false;

        foreach (var symbol in deletedSymbols)
        {
            resolvedPaths.Add(symbol.RelativePath.Replace('\\', '/'));
            changedRefs.Add(ToChangedSymbolRef(symbol, isTestSymbol));
        }

        foreach (var symbol in deletedSymbols)
        {
            var offset = 0;
            var fetched = 0;
            while (fetched < budget.CallerMaxResults)
            {
                var limit = Math.Min(budget.CallerPageSize, budget.CallerMaxResults - fetched);
                var page = reader.CallerRelationsPaged(symbol, limit, offset, budget.MinConfidence);

                if (page.Items.Count == 0)
                {
                    // 결과가 더 있다고 하면서 아무것도 주지 않는 페이지는
                    // 더 순회할 수 없다. 완전함이 아니라 절단으로 본다.
                    if (page.HasMore)
                    {
                        callerPotentiallyTruncated = true;
                    }

                    break;
                }

                foreach (var relation in page.Items)
                {
                    callers.Add(new CallerRelation(
                        symbol.Id,
                        relation.Symbol.Id,
                        relation.Symbol.Project,
                        relation.Symbol.RelativePath,
                        relation.Edge.Kind.ToString(),
                        relation.Edge.Confidence,
                        isTestSymbol(relation.Symbol)));
                    impacted.Add(new ImpactedSymbolRef(
                        relation.Symbol.Id,
                        relation.Symbol.Project,
                        relation.Symbol.RelativePath,
                        SymbolDisplayName.For(relation.Symbol),
                        symbol.Id,
                        1,
                        isTestSymbol(relation.Symbol),
                        relation.Edge.ResolutionKind.ToString(),
                        relation.Edge.Confidence));
                    fetched++;
                }

                offset += page.Items.Count;
                if (!page.HasMore)
                {
                    break;
                }

                if (fetched >= budget.CallerMaxResults)
                {
                    // base 그래프에 호출자가 더 남아 있는데
                    // 심볼당 호출자 예산이 소진되었다.
                    callerPotentiallyTruncated = true;
                    break;
                }
            }
        }

        if (resolvedPaths.Count == 0)
        {
            return null;
        }

        return new DeletionImpactResult(
            resolvedPaths.ToArray(),
            changedRefs.Where(item => !headIds.Contains(item.Id)).ToArray(),
            callers,
            impacted,
            impactPotentiallyTruncated,
            callerPotentiallyTruncated);
    }

    private static ChangedSymbolRef ToChangedSymbolRef(IndexedSymbol symbol, Func<IndexedSymbol, bool> isTestSymbol) =>
        new(
            symbol.Id,
            symbol.Project,
            symbol.RelativePath,
            SymbolDisplayName.For(symbol),
            symbol.StartLine,
            symbol.EndLine,
            string.Equals(symbol.Visibility, "public", StringComparison.OrdinalIgnoreCase),
            isTestSymbol(symbol));
}

/// <summary>
/// 선택적(analysis.indexBaseRevision) 경로. base 리비전을 임시 git worktree로
/// 체크아웃하고, 별도 CodeMap 데이터베이스에 인덱싱한 뒤,
/// 그 인덱스로 삭제 분석을 다시 실행한다. 실패하면 삭제는
/// unresolved로 남고(플래너는 blocking constraint를 유지한다) 아무것도 조용히
/// 의미적 사실로 승격되지 않는다.
/// </summary>
internal static class BaseRevisionIndex
{
    /// <summary>
    /// 불변 base 리비전의 CodeMap 데이터베이스를 반환한다.
    /// <c>.proof/base-index/{sha}</c>를 재사용하거나 git worktree에서 한 번 만든다.
    /// </summary>
    public static async Task<string?> EnsureBaseIndexDatabaseAsync(
        string workspaceRoot,
        string baseRevision,
        CancellationToken cancellationToken,
        bool forceRebuild = false)
    {
        if (!PublicSurfaceSnapshotStore.IsResolvableRevision(baseRevision))
        {
            return null;
        }

        var cachedDatabase = GetCachedDatabasePath(workspaceRoot, baseRevision);
        var cacheRoot = Path.GetDirectoryName(Path.GetDirectoryName(cachedDatabase))!;
        if (!forceRebuild && File.Exists(cachedDatabase))
        {
            return cachedDatabase;
        }

        if (forceRebuild)
        {
            try
            {
                if (Directory.Exists(cacheRoot))
                {
                    Directory.Delete(cacheRoot, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        var worktree = Path.Combine(Path.GetTempPath(), "proof-baserev-" + Guid.NewGuid().ToString("N"));
        try
        {
            var added = await RunGitAsync(workspaceRoot, ["worktree", "add", "--detach", worktree, baseRevision], cancellationToken).ConfigureAwait(false);
            if (!added)
            {
                return null;
            }

            await IncrementalCodeMapIndexerFactory.Create().IndexAsync(worktree, force: true, cancellationToken).ConfigureAwait(false);
            var database = CodeMapIndexLocator.FindDatabase(worktree);
            TryCacheDatabase(database, cacheRoot);
            return File.Exists(cachedDatabase) ? cachedDatabase : database;
        }
        catch (Exception exception) when (exception is IOException
            or FileNotFoundException or DirectoryNotFoundException
            or InvalidOperationException or ArgumentException)
        {
            return null;
        }
        finally
        {
            await RunGitAsync(workspaceRoot, ["worktree", "remove", "--force", worktree], CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (Directory.Exists(worktree))
                {
                    Directory.Delete(worktree, recursive: true);
                }
            }
            catch (IOException)
            {
                // 최선의 정리. worktree remove가 이미 분리했다.
            }
        }
    }

    public static async Task<DeletionImpactResult?> AnalyzeDeletionImpactAsync(
        string workspaceRoot,
        string baseRevision,
        IReadOnlyList<string> deletedPaths,
        ResolvedImpactBudget budget,
        CancellationToken cancellationToken,
        Func<IndexedSymbol, bool>? isTestSymbol = null)
    {
        if (deletedPaths.Count == 0)
        {
            return null;
        }

        var database = await EnsureBaseIndexDatabaseAsync(workspaceRoot, baseRevision, cancellationToken).ConfigureAwait(false);
        if (database is null)
        {
            return null;
        }

        var result = await TryAnalyzeFromDatabaseAsync(database, deletedPaths, budget, cancellationToken, isTestSymbol).ConfigureAwait(false);
        if (result is not null)
        {
            return result;
        }

        // 캐시된 인덱스가 손상되었거나 오래되었을 수 있다. 한 번 다시 만들고 재시도한다.
        var cacheRoot = Path.GetDirectoryName(Path.GetDirectoryName(GetCachedDatabasePath(workspaceRoot, baseRevision)))!;
        if (File.Exists(database))
        {
            try
            {
                if (Directory.Exists(cacheRoot))
                {
                    Directory.Delete(cacheRoot, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }

        database = await EnsureBaseIndexDatabaseAsync(workspaceRoot, baseRevision, cancellationToken).ConfigureAwait(false);
        if (database is null)
        {
            return null;
        }

        return await TryAnalyzeFromDatabaseAsync(database, deletedPaths, budget, cancellationToken, isTestSymbol).ConfigureAwait(false);
    }

    private static async Task<DeletionImpactResult?> TryAnalyzeFromDatabaseAsync(
        string database,
        IReadOnlyList<string> deletedPaths,
        ResolvedImpactBudget budget,
        CancellationToken cancellationToken,
        Func<IndexedSymbol, bool>? isTestSymbol)
    {
        try
        {
            var connection = await new CodeMapQueryStore(database)
                .OpenReadOnlyConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var reader = new SqliteCodeMapGraphReader(connection);
            return DeletionImpactAnalyzer.Analyze(reader, deletedPaths, budget, [], isTestSymbol);
        }
        catch (Exception exception) when (exception is IOException
            or FileNotFoundException or DirectoryNotFoundException
            or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private static void TryCacheDatabase(string database, string cacheRoot)
    {
        try
        {
            var source = Path.GetDirectoryName(database);
            if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source))
            {
                return;
            }

            var target = Path.Combine(cacheRoot, ".codemap");
            if (Directory.Exists(cacheRoot))
            {
                Directory.Delete(cacheRoot, recursive: true);
            }

            Directory.CreateDirectory(target);
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, file);
                var destination = Path.Combine(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, overwrite: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 캐싱은 최선 노력이다. 새로 만든 worktree 인덱스는 그래도 쓸 수 있다.
        }
    }

    internal static string GetCachedDatabasePath(string workspaceRoot, string baseRevision)
        => Path.Combine(workspaceRoot, ".proof", "base-index", Sanitize(baseRevision), ".codemap", "index.db");

    private static string Sanitize(string value)
        => new(value.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_').ToArray());

    private static async Task<bool> RunGitAsync(string workspaceRoot, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workspaceRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return false;
        }

        // 프로세스가 도는 동안 두 파이프를 비운다. git은 Windows의 작은
        // 파이프 버퍼보다 많이 쓸 수 있고(예: worktree/fetch 진행),
        // 읽지 않고 기다리면 교착된다.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        return process.ExitCode == 0;
    }
}
