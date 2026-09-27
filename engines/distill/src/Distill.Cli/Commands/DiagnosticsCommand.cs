using System.CommandLine;
using System.Text.Json;
using Distill.Core.Runs;

namespace Distill.Cli.Commands;

public static class DiagnosticsCommand
{
    public static Command Create()
    {
        var kindOption = new Option<string?>("--kind")
        {
            Description = "Filter diagnostics by kind (build, test, format, infrastructure)."
        };

        var runOption = new Option<string?>("--run")
        {
            Description = "Run id to inspect. Defaults to latest run."
        };

        var command = new Command("diagnostics", "Show structured diagnostics from a verification run.")
        {
            kindOption,
            runOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var kind = parseResult.GetValue(kindOption);
            var runId = parseResult.GetValue(runOption);
            return await ExecuteAsync(kind, runId, cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    internal static async Task<int> ExecuteAsync(
        string? kind,
        string? runId,
        CancellationToken cancellationToken)
    {
        var workspaceRoot = Directory.GetCurrentDirectory();
        var resolved = await RunContextResolver.ResolveLatestAsync(workspaceRoot, runId, cancellationToken)
            .ConfigureAwait(false);

        if (resolved is null)
        {
            Console.Error.WriteLine("No verification run found.");
            return ExitCodeMapper.ConfigError;
        }

        var normalizedPath = Distill.Core.Runs.RunArtifactLayout.GetNormalizedPath(resolved.Value.RunDirectory);
        if (!File.Exists(normalizedPath))
        {
            Console.Error.WriteLine($"Normalized diagnostics not found: {normalizedPath}");
            return ExitCodeMapper.FromStatus(Distill.Core.Runs.VerificationStatus.InfraError);
        }

        var json = await File.ReadAllTextAsync(normalizedPath, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        if (!TryResolveNormalizedVersion(document.RootElement, out var versionError))
        {
            Console.Error.WriteLine(versionError);
            return ExitCodeMapper.FromStatus(VerificationStatus.InfraError);
        }

        if (string.IsNullOrWhiteSpace(kind))
        {
            Console.WriteLine(json);
            return 0;
        }

        if (!document.RootElement.TryGetProperty("diagnostics", out var diagnosticsElement))
        {
            Console.WriteLine("[]");
            return 0;
        }

        var filtered = diagnosticsElement.EnumerateArray()
            .Where(item =>
            {
                if (!item.TryGetProperty("Diagnostic", out var diagnostic)
                    && !item.TryGetProperty("diagnostic", out diagnostic))
                {
                    return false;
                }

                if (!diagnostic.TryGetProperty("Kind", out var kindElement)
                    && !diagnostic.TryGetProperty("kind", out kindElement))
                {
                    return false;
                }

                return string.Equals(kindElement.GetString(), kind, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        Console.WriteLine(JsonSerializer.Serialize(filtered, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    internal static bool TryResolveNormalizedVersion(JsonElement root, out string? error)
    {
        if (!root.TryGetProperty("version", out var versionElement))
        {
            error = null;
            return true;
        }

        if (versionElement.ValueKind == JsonValueKind.Number
            && versionElement.TryGetInt32(out var version)
            && version == 1)
        {
            error = null;
            return true;
        }

        if (versionElement.ValueKind == JsonValueKind.String
            && string.Equals(versionElement.GetString(), "1", StringComparison.Ordinal))
        {
            error = null;
            return true;
        }

        error = $"Unsupported normalized.json version: {versionElement}.";
        return false;
    }
}
