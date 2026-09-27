namespace Distill.Core.Runs;

public sealed record DistillState(
    string? LastRunId,
    string? LastRunDirectory,
    DateTimeOffset? LastRunAt);

public static class DistillStateStore
{
    private static readonly System.Text.Json.JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public static async Task<DistillState?> LoadAsync(string workspaceRoot, CancellationToken cancellationToken = default)
    {
        var path = RunArtifactLayout.GetStatePath(workspaceRoot);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return System.Text.Json.JsonSerializer.Deserialize<DistillState>(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    public static async Task SaveAsync(
        string workspaceRoot,
        DistillState state,
        CancellationToken cancellationToken = default)
    {
        var path = RunArtifactLayout.GetStatePath(workspaceRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = System.Text.Json.JsonSerializer.Serialize(state, SerializerOptions);
        await AtomicFileWriter.WriteTextAsync(path, json, cancellationToken).ConfigureAwait(false);
    }

    public static async Task SaveLatestAsync(
        string workspaceRoot,
        DistillState candidate,
        CancellationToken cancellationToken = default)
    {
        var path = RunArtifactLayout.GetStatePath(workspaceRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var candidateJson = System.Text.Json.JsonSerializer.Serialize(candidate, SerializerOptions);

        await AtomicFileWriter.UpdateTextAsync(
            path,
            currentText =>
            {
                if (TryDeserializeState(currentText, out var current)
                    && current!.LastRunAt is not null
                    && candidate.LastRunAt is not null
                    && current.LastRunAt > candidate.LastRunAt)
                {
                    return null;
                }

                return candidateJson;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static bool TryDeserializeState(string? json, out DistillState? state)
    {
        state = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            state = System.Text.Json.JsonSerializer.Deserialize<DistillState>(json);
            return state is not null;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }
}
