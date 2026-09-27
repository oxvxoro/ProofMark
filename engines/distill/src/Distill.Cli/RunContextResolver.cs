using System.Text.Json;
using Distill.Core.Runs;

namespace Distill.Cli;

public static class RunContextResolver
{
    public static async Task<(string RunDirectory, string RunId)?> ResolveLatestAsync(
        string workspaceRoot,
        string? runIdOverride = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(runIdOverride))
        {
            var explicitDirectory = RunArtifactLayout.GetRunDirectory(workspaceRoot, runIdOverride);
            return Directory.Exists(explicitDirectory)
                ? (explicitDirectory, runIdOverride)
                : null;
        }

        var state = await DistillStateStore.LoadAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        if (state?.LastRunDirectory is not null && Directory.Exists(state.LastRunDirectory))
        {
            return (state.LastRunDirectory, state.LastRunId ?? Path.GetFileName(state.LastRunDirectory));
        }

        var runsRoot = RunArtifactLayout.GetRunsRoot(workspaceRoot);
        if (!Directory.Exists(runsRoot))
        {
            return null;
        }

        var latestCompleted = Directory
            .EnumerateDirectories(runsRoot)
            .Select(path => (Path: path, FinishedAt: TryReadFinishedAt(path)))
            .Where(entry => entry.FinishedAt is not null)
            .OrderByDescending(entry => entry.FinishedAt)
            .Select(entry => entry.Path)
            .FirstOrDefault();

        if (latestCompleted is not null)
        {
            return (latestCompleted, Path.GetFileName(latestCompleted));
        }

        var latest = Directory
            .EnumerateDirectories(runsRoot)
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return latest is null ? null : (latest, Path.GetFileName(latest));
    }

    private static DateTimeOffset? TryReadFinishedAt(string runDirectory)
    {
        var manifestPath = RunArtifactLayout.GetRunManifestPath(runDirectory);
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<RunManifest>(json, new JsonSerializerOptions
            {
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            });

            return manifest?.FinishedAt;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
