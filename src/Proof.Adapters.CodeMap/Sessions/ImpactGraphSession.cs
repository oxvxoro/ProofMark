using CodeMap.Core.Contracts;
using CodeMap.Storage;
using CodeMap.Storage.Queries;

namespace Proof.Adapters.CodeMap.Sessions;

internal static class ImpactGraphSession
{
    internal static async Task<ICodeMapGraphReader> OpenGraphReaderAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        var connection = await new CodeMapQueryStore(databasePath).OpenReadOnlyConnectionAsync(cancellationToken);
        return new SqliteCodeMapGraphReader(connection);
    }
}
