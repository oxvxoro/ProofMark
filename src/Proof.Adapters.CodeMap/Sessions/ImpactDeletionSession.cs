using CodeMap.Core.Contracts;
using CodeMap.Core.Models;
using CodeMap.Storage;
using CodeMap.Storage.Queries;
using Proof.Core;

namespace Proof.Adapters.CodeMap.Sessions;

internal static class ImpactDeletionSession
{
    internal static async Task<DeletionImpactResult?> AnalyzeAsync(
        string indexInput,
        string workspaceRoot,
        string baseRevision,
        IReadOnlyList<string> deletedPaths,
        ResolvedImpactBudget budget,
        bool indexBaseRevision,
        Func<IndexedSymbol, bool> isTestSymbol,
        CancellationToken cancellationToken)
    {
        if (deletedPaths.Count == 0)
        {
            return null;
        }

        try
        {
            var database = CodeMapIndexLocator.FindDatabase(indexInput);
            await using var reader = await ImpactGraphSession.OpenGraphReaderAsync(database, cancellationToken).ConfigureAwait(false);
            var result = DeletionImpactAnalyzer.Analyze(reader, deletedPaths, budget, [], isTestSymbol);
            if (result is not null)
            {
                return result;
            }
        }
        catch (Exception exception) when (exception is IOException or FileNotFoundException or DirectoryNotFoundException or InvalidOperationException)
        {
            // 갱신 전 인덱스가 없다. 선택적 base-revision 경로로 넘어가고,
            // 그렇지 않으면 삭제를 unresolved로 유지한다.
        }

        if (!indexBaseRevision)
        {
            return null;
        }

        return await BaseRevisionIndex.AnalyzeDeletionImpactAsync(
            workspaceRoot,
            baseRevision,
            deletedPaths,
            budget,
            cancellationToken,
            isTestSymbol).ConfigureAwait(false);
    }
}
