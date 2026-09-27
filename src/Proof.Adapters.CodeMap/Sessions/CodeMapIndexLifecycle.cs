using CodeMap.Storage;
using Proof.Core;

namespace Proof.Adapters.CodeMap.Sessions;

internal static class CodeMapIndexLifecycle
{
    internal static bool ShouldSkipIndexUpdate(string workspaceRoot, ChangeRequest request, string indexInput)
        => ImpactIndexSession.ShouldSkipIndexUpdate(workspaceRoot, request, indexInput);

    internal static Task UpdateIndexAsync(
        Func<IncrementalCodeMapIndexer> indexerFactory,
        string indexInput,
        CancellationToken cancellationToken)
        => ImpactIndexSession.UpdateIndexAsync(indexerFactory, indexInput, cancellationToken);

    internal static void WriteIndexStamp(string workspaceRoot, ChangeRequest request, string indexInput)
        => ImpactIndexSession.WriteIndexStamp(workspaceRoot, request, indexInput);
}
