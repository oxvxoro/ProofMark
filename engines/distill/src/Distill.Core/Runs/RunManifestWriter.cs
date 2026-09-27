namespace Distill.Core.Runs;

public sealed record RunManifest(
    string RunId,
    DateTimeOffset StartedAt,
    string WorkspaceRoot,
    DateTimeOffset? FinishedAt = null,
    VerificationStatus? Status = null,
    long RawBytes = 0,
    long CompactBytes = 0);

public static class RunManifestWriter
{
    public static async Task WriteAsync(string runDirectory, RunManifest manifest, CancellationToken cancellationToken = default)
    {
        RunArtifactLayout.EnsureRunDirectories(runDirectory);

        var path = RunArtifactLayout.GetRunManifestPath(runDirectory);
        var json = System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        });

        await AtomicFileWriter.WriteTextAsync(path, json, cancellationToken).ConfigureAwait(false);
    }
}
