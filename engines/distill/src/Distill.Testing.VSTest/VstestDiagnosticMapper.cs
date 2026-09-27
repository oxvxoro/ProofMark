using System.Text.RegularExpressions;
using Distill.Core.Diagnostics;
using Distill.Core.Evidence;

namespace Distill.Testing.VSTest;

public static partial class VstestDiagnosticMapper
{
    [GeneratedRegex(@"^\s*at (.+?) in (.+?):line (\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex StackFrameWithFileRegex();

    [GeneratedRegex(@"^\s*at (.+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex StackFrameWithoutFileRegex();

    public static IReadOnlyList<DistillDiagnostic> MapFailedCases(
        IEnumerable<TestCaseEvidence> cases,
        DiagnosticProvenance provenance,
        double confidence = 1.0)
    {
        var diagnostics = new List<DistillDiagnostic>();
        var index = 0;

        foreach (var testCase in cases)
        {
            if (!string.Equals(testCase.Outcome, "failed", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            index++;
            diagnostics.Add(MapFailedCase(testCase, provenance, confidence, index));
        }

        return diagnostics;
    }

    public static DistillDiagnostic MapFailedCase(
        TestCaseEvidence testCase,
        DiagnosticProvenance provenance,
        double confidence,
        int index)
    {
        var frames = ParseStackFrames(testCase.Stack);
        ExceptionEvidence? exception = null;
        if (!string.IsNullOrWhiteSpace(testCase.Message))
        {
            var messageLines = testCase.Message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            exception = new ExceptionEvidence(
                messageLines.Length > 0 ? messageLines[0] : "TestFailure",
                testCase.Message);
        }

        return DistillDiagnostic.Create(
            id: $"test-error-{index}",
            kind: DiagnosticKind.Test,
            severity: DiagnosticSeverity.Error,
            source: "vstest",
            code: "TEST_FAILED",
            message: testCase.Message ?? $"Test '{testCase.Name}' failed.",
            testName: testCase.Name,
            exception: exception,
            frames: frames,
            provenance: provenance,
            confidence: confidence);
    }

    public static IReadOnlyList<StackFrameEvidence> ParseStackFrames(string? stackTrace)
    {
        if (string.IsNullOrWhiteSpace(stackTrace))
        {
            return Array.Empty<StackFrameEvidence>();
        }

        var frames = new List<StackFrameEvidence>();
        foreach (var line in stackTrace.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var withFile = StackFrameWithFileRegex().Match(line);
            if (withFile.Success)
            {
                var method = withFile.Groups[1].Value;
                var file = withFile.Groups[2].Value.Replace('\\', '/');
                var lineNumber = int.Parse(withFile.Groups[3].Value);
                frames.Add(new StackFrameEvidence(file, lineNumber, method, IsFrameworkMethod(method)));
                continue;
            }

            var withoutFile = StackFrameWithoutFileRegex().Match(line);
            if (withoutFile.Success)
            {
                var method = withoutFile.Groups[1].Value;
                frames.Add(new StackFrameEvidence(null, 0, method, IsFrameworkMethod(method)));
            }
        }

        return frames;
    }

    private static bool IsFrameworkMethod(string method)
        => method.StartsWith("System.", StringComparison.Ordinal)
           || method.StartsWith("Microsoft.", StringComparison.Ordinal)
           || method.StartsWith("xunit.", StringComparison.OrdinalIgnoreCase);
}
