using System.Text.Json;
using Distill.Core.Diagnostics;
using Distill.Core.Evidence;
using Distill.Core.Runs;

namespace Distill.Testing.VSTest;

internal static class TestDiagnosticsWriter
{
    public static async Task WriteAsync(
        string checkDirectory,
        TestRunEvidence evidence,
        IReadOnlyList<DistillDiagnostic> diagnostics,
        string source,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            source,
            passed = evidence.Passed,
            failed = evidence.Failed,
            skipped = evidence.Skipped,
            durationMs = evidence.Duration.TotalMilliseconds,
            diagnostics
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(
            Path.Combine(checkDirectory, "diagnostics.json"),
            json,
            cancellationToken).ConfigureAwait(false);
    }
}
