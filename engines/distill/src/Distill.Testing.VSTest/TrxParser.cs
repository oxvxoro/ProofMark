using System.Globalization;
using System.Xml.Linq;
using Distill.Core.Evidence;

namespace Distill.Testing.VSTest;

public sealed class TrxParser
{
    public TestRunEvidence Parse(string trxPath, string sourceId = "vstest-trx", string? project = null)
    {
        var document = XDocument.Load(trxPath);
        var ns = document.Root?.Name.Namespace ?? XNamespace.None;

        var unitTests = document
            .Descendants(ns + "UnitTest")
            .ToDictionary(
                item => item.Attribute("id")?.Value ?? string.Empty,
                item => item.Attribute("name")?.Value ?? string.Empty,
                StringComparer.Ordinal);

        var results = document
            .Descendants(ns + "UnitTestResult")
            .ToList();

        var cases = new List<TestCaseEvidence>();
        int passed = 0;
        int failed = 0;
        int skipped = 0;
        double totalDurationMs = 0;

        foreach (var result in results)
        {
            var testId = result.Attribute("testId")?.Value ?? string.Empty;
            var name = result.Attribute("testName")?.Value
                       ?? (unitTests.TryGetValue(testId, out var definitionName) ? definitionName : "unknown");
            var outcome = MapOutcome(result.Attribute("outcome")?.Value);
            var durationMs = ParseDurationMs(result.Attribute("duration")?.Value);
            var output = result.Element(ns + "Output");
            var errorInfo = output?.Element(ns + "ErrorInfo");
            var message = errorInfo?.Element(ns + "Message")?.Value;
            var stack = errorInfo?.Element(ns + "StackTrace")?.Value;
            var resolvedProject = project ?? InferProject(name);

            cases.Add(new TestCaseEvidence(
                name,
                outcome,
                message,
                stack,
                durationMs,
                FullyQualifiedName: name,
                Project: resolvedProject));
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
        }

        return new TestRunEvidence(
            cases,
            passed,
            failed,
            skipped,
            TimeSpan.FromMilliseconds(totalDurationMs),
            sourceId);
    }

    private static string? InferProject(string testName)
    {
        var separator = testName.IndexOf('.');
        return separator > 0 ? testName[..separator] : null;
    }

    private static string MapOutcome(string? outcome)
        => outcome?.ToLowerInvariant() switch
        {
            "passed" => "passed",
            "failed" => "failed",
            "notexecuted" => "skipped",
            "skipped" => "skipped",
            _ => "none"
        };

    private static double ParseDurationMs(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
        {
            return 0;
        }

        return TimeSpan.TryParse(duration, CultureInfo.InvariantCulture, out var value)
            ? value.TotalMilliseconds
            : 0;
    }
}
