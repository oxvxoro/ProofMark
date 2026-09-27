using System.Text.Json;
using Distill.Core.Diagnostics;
using Distill.Core.Evidence;

namespace Distill.Testing.VSTest;

public sealed record JsonlParseResult(
    TestRunEvidence? Evidence,
    IReadOnlyList<DistillDiagnostic> Diagnostics,
    bool IsComplete,
    string? ErrorMessage);

public sealed class JsonlTestResultParser
{
    public JsonlParseResult Parse(string eventsPath, string sourceId = "vstest-logger", string? project = null)
    {
        if (!File.Exists(eventsPath))
        {
            return new JsonlParseResult(null, Array.Empty<DistillDiagnostic>(), false, "JSONL file was not created.");
        }

        var lines = File.ReadAllLines(eventsPath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        if (lines.Count == 0)
        {
            return new JsonlParseResult(null, Array.Empty<DistillDiagnostic>(), false, "JSONL file is empty.");
        }

        var cases = new List<TestCaseEvidence>();
        var hasRunStart = false;
        var hasRunComplete = false;
        int passed = 0;
        int failed = 0;
        int skipped = 0;
        double totalDurationMs = 0;

        foreach (var line in lines)
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var typeProperty))
            {
                continue;
            }

            var type = typeProperty.GetString();
            switch (type)
            {
                case "run-start":
                    hasRunStart = true;
                    break;
                case "test-result":
                    var name = root.GetProperty("name").GetString() ?? "unknown";
                    var outcome = root.GetProperty("outcome").GetString() ?? "none";
                    var durationMs = root.TryGetProperty("durationMs", out var durationProperty)
                        ? durationProperty.GetDouble()
                        : 0;
                    var error = root.TryGetProperty("error", out var errorProperty)
                        ? errorProperty.GetString()
                        : null;
                    var stack = root.TryGetProperty("stack", out var stackProperty)
                        ? stackProperty.GetString()
                        : null;

                    cases.Add(new TestCaseEvidence(
                        name,
                        outcome,
                        error,
                        stack,
                        durationMs,
                        FullyQualifiedName: name,
                        Project: project));
                    totalDurationMs += durationMs;

                    switch (outcome)
                    {
                        case "passed":
                            passed++;
                            break;
                        case "failed":
                            failed++;
                            break;
                        case "skipped":
                            skipped++;
                            break;
                    }

                    break;
                case "run-complete":
                    hasRunComplete = true;
                    passed = root.GetProperty("passed").GetInt32();
                    failed = root.GetProperty("failed").GetInt32();
                    skipped = root.GetProperty("skipped").GetInt32();
                    break;
            }
        }

        var isComplete = hasRunStart && hasRunComplete;
        if (!isComplete)
        {
            return new JsonlParseResult(
                null,
                VstestDiagnosticMapper.MapFailedCases(cases, DiagnosticProvenance.VSTestLoggerEvent),
                false,
                "JSONL stream is incomplete (missing run-start or run-complete).");
        }

        var evidence = new TestRunEvidence(
            cases,
            passed,
            failed,
            skipped,
            TimeSpan.FromMilliseconds(totalDurationMs),
            sourceId);

        return new JsonlParseResult(
            evidence,
            VstestDiagnosticMapper.MapFailedCases(cases, DiagnosticProvenance.VSTestLoggerEvent),
            true,
            null);
    }
}
