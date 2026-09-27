using System.Text.RegularExpressions;
using Distill.Core.Diagnostics;

namespace Distill.Reporting;

public static partial class FormatDiagnosticParser
{
    [GeneratedRegex(@"(?<file>[^\s:(]+\.cs)(?:\((?<line>\d+),(?<column>\d+)\))?", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex FileLocationRegex();

    public static IReadOnlyList<DistillDiagnostic> Parse(
        IEnumerable<string> lines,
        string source = "dotnet-format")
    {
        var diagnostics = new List<DistillDiagnostic>();
        var index = 0;
        foreach (var line in lines)
        {
            if (!line.Contains(".cs", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("format", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            index++;
            var match = FileLocationRegex().Match(line);
            SourceLocation? location = null;
            if (match.Success)
            {
                location = new SourceLocation(
                    match.Groups["file"].Value,
                    match.Groups["line"].Success ? int.Parse(match.Groups["line"].Value) : 0,
                    match.Groups["column"].Success ? int.Parse(match.Groups["column"].Value) : 0);
            }

            diagnostics.Add(DistillDiagnostic.Create(
                id: $"format-{index}",
                kind: DiagnosticKind.Format,
                severity: DiagnosticSeverity.Error,
                source: source,
                code: "FORMAT_VIOLATION",
                message: line.Trim(),
                location: location,
                provenance: DiagnosticProvenance.KnownTextParser,
                confidence: location is null ? 0.7 : 0.8));
        }

        return diagnostics;
    }
}
