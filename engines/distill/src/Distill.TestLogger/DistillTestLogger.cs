using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;

namespace Distill.TestLogger;

[ExtensionUri("logger://distill/v1")]
[FriendlyName("distill")]
public sealed class DistillTestLogger : ITestLoggerWithParameters
{
    private JsonlEventWriter? _writer;

    public void Initialize(TestLoggerEvents events, string? parameters)
    {
        var normalized = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in ParseLegacyParameters(parameters))
        {
            normalized[pair.Key] = pair.Value;
        }

        Initialize(events, normalized);
    }

    public void Initialize(TestLoggerEvents events, Dictionary<string, string?> parameters)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in parameters)
        {
            normalized[pair.Key] = pair.Value ?? string.Empty;
        }

        InitializeCore(events, normalized);
    }

    private void InitializeCore(TestLoggerEvents events, Dictionary<string, string> parameters)
    {
        var outputPath = parameters.TryGetValue("Output", out var output) && !string.IsNullOrWhiteSpace(output)
            ? output
            : Path.Combine(Directory.GetCurrentDirectory(), "tests.events.jsonl");

        _writer = new JsonlEventWriter(outputPath);
        _writer.WriteRunStart(DateTimeOffset.UtcNow);

        events.TestResult += (_, args) =>
        {
            var result = args.Result;
            _writer!.WriteTestResult(
                result.DisplayName ?? "unknown",
                MapOutcome(result.Outcome),
                result.Duration.TotalMilliseconds,
                result.ErrorMessage,
                ExtractStackTrace(result));
        };

        events.TestRunComplete += (_, args) =>
        {
            var stats = args.TestRunStatistics;
            var passed = CountOutcome(stats, TestOutcome.Passed);
            var failed = CountOutcome(stats, TestOutcome.Failed);
            var skipped = CountOutcome(stats, TestOutcome.Skipped);
            _writer!.WriteRunComplete(
                passed + failed + skipped,
                passed,
                failed,
                skipped);
            _writer.Dispose();
            _writer = null;
        };
    }

    private static int CountOutcome(ITestRunStatistics? stats, TestOutcome outcome)
        => stats is null ? 0 : (int)stats[outcome];

    private static string? ExtractStackTrace(TestResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.ErrorStackTrace))
        {
            return result.ErrorStackTrace;
        }

        var stackMessages = result.Messages
            .Where(message => string.Equals(message.Category, "Stack Trace", StringComparison.OrdinalIgnoreCase))
            .Select(message => message.Text ?? string.Empty)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        return stackMessages.Count == 0 ? null : string.Join(Environment.NewLine, stackMessages);
    }

    private static Dictionary<string, string> ParseLegacyParameters(string? parameters)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(parameters))
        {
            return map;
        }

        foreach (var segment in parameters!.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = segment.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = segment.Substring(0, separatorIndex).Trim();
            var value = segment.Substring(separatorIndex + 1).Trim();
            map[key] = value;
        }

        return map;
    }

    private static string MapOutcome(TestOutcome outcome)
        => outcome switch
        {
            TestOutcome.Passed => "passed",
            TestOutcome.Failed => "failed",
            TestOutcome.Skipped => "skipped",
            _ => "none"
        };
}
