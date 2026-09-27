using System.Text.Json;
using CodeMap.Storage;
using Proof.Core;

namespace Proof.Adapters.CodeMap.Sessions;

internal static class ImpactIndexSession
{
    internal static string IndexStampPath(string workspaceRoot)
        => Path.Combine(workspaceRoot, ".proof", "index-stamp.json");

    internal sealed record IndexStamp(
        string SourceDigest,
        string IndexInput,
        string DatabasePath,
        long DatabaseLength,
        DateTime DatabaseWriteTimeUtc);

    private static readonly JsonSerializerOptions ProofJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    internal static bool ShouldSkipIndexUpdate(string workspaceRoot, ChangeRequest request, string indexInput)
    {
        if (request.IsDirty || string.IsNullOrWhiteSpace(request.SourceDigest))
        {
            return false;
        }

        var stamp = TryReadIndexStamp(IndexStampPath(workspaceRoot));
        if (stamp is null
            || !string.Equals(stamp.SourceDigest, request.SourceDigest, StringComparison.Ordinal)
            || !string.Equals(stamp.IndexInput, Path.GetFullPath(indexInput), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var database = CodeMapIndexLocator.FindDatabase(indexInput);
            var info = new FileInfo(database);
            return info.Exists
                   && string.Equals(stamp.DatabasePath, database, StringComparison.OrdinalIgnoreCase)
                   && stamp.DatabaseLength == info.Length
                   && stamp.DatabaseWriteTimeUtc == info.LastWriteTimeUtc;
        }
        catch (Exception exception) when (exception is FileNotFoundException or IOException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    internal static IndexStamp? TryReadIndexStamp(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<IndexStamp>(File.ReadAllText(path), ProofJsonOptions);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static void WriteIndexStamp(string workspaceRoot, ChangeRequest request, string indexInput)
    {
        if (string.IsNullOrWhiteSpace(request.SourceDigest))
        {
            return;
        }

        try
        {
            var database = CodeMapIndexLocator.FindDatabase(indexInput);
            var info = new FileInfo(database);
            var stamp = new IndexStamp(
                request.SourceDigest,
                Path.GetFullPath(indexInput),
                database,
                info.Length,
                info.LastWriteTimeUtc);
            var path = IndexStampPath(workspaceRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(stamp, ProofJsonOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        {
            // stamp는 최적화다. 쓰기 실패는 캐싱만 잃는다.
        }
    }

    internal static async Task UpdateIndexAsync(
        Func<IncrementalCodeMapIndexer> indexerFactory,
        string indexInput,
        CancellationToken cancellationToken)
    {
        try
        {
            await indexerFactory().UpdateAsync(indexInput, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or FileNotFoundException or DirectoryNotFoundException or ArgumentException)
        {
            throw new ProofConfigException(exception.Message, exception);
        }
    }
}
