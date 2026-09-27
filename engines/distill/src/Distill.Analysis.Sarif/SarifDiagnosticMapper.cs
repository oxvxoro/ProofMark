using System.Text.Json;
using Distill.Core.Diagnostics;

namespace Distill.Analysis.Sarif;

public static class SarifDiagnosticMapper
{
    public static IReadOnlyList<DistillDiagnostic> Map(string sarifPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(sarifPath));
        return Map(document.RootElement);
    }

    public static IReadOnlyList<DistillDiagnostic> Map(JsonElement root)
    {
        var diagnostics = new List<DistillDiagnostic>();
        if (!root.TryGetProperty("runs", out var runs))
        {
            return diagnostics;
        }

        var index = 0;
        foreach (var run in runs.EnumerateArray())
        {
            var toolName = "sarif";
            if (run.TryGetProperty("tool", out var tool)
                && tool.TryGetProperty("driver", out var driver)
                && driver.TryGetProperty("name", out var name))
            {
                toolName = name.GetString() ?? toolName;
            }

            // 진단의 프로젝트 출처. Proof는 이 필드로 프로젝트 범위
            // 정적 분석 증거를 묶으므로 SARIF
            // 수집을 거쳐도 남아야 한다. 결과 속성이 실행 수준 출처를 덮어쓴다.
            var runProject = GetRunProject(run);

            if (!run.TryGetProperty("results", out var results))
            {
                continue;
            }

            foreach (var result in results.EnumerateArray())
            {
                index++;
                var ruleId = GetString(result, "ruleId");
                var level = GetString(result, "level") ?? "warning";
                var message = GetMessage(result);
                var location = GetLocation(result);
                var project = GetStringFromProperties(result, "project") ?? runProject;

                diagnostics.Add(DistillDiagnostic.Create(
                    id: $"sarif-{index}",
                    kind: DiagnosticKind.Analysis,
                    severity: MapSeverity(level),
                    source: toolName,
                    code: ruleId,
                    message: message,
                    location: location,
                    project: project,
                    provenance: DiagnosticProvenance.Sarif,
                    confidence: location is null ? 0.8 : 1.0,
                    properties: new Dictionary<string, string>
                    {
                        ["tool"] = toolName
                    }));
            }
        }

        return diagnostics;
    }

    private static string? GetRunProject(JsonElement run)
    {
        var fromProperties = GetStringFromProperties(run, "project");
        if (!string.IsNullOrWhiteSpace(fromProperties))
        {
            return fromProperties;
        }

        if (run.TryGetProperty("automationDetails", out var automationDetails)
            && automationDetails.TryGetProperty("id", out var id))
        {
            var value = id.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                // 자동화 id는 "checkId/ProjectName" 또는
                // "run/ProjectName" 형태이며, 프로젝트는 마지막 구간이다.
                var parts = value.Split(
                    '/',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return parts.Length > 0 ? parts[^1] : null;
            }
        }

        return null;
    }

    private static string? GetStringFromProperties(JsonElement element, string key)
    {
        if (element.TryGetProperty("properties", out var properties)
            && properties.TryGetProperty(key, out var value)
            && value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static string GetMessage(JsonElement result)
    {
        if (result.TryGetProperty("message", out var message)
            && message.TryGetProperty("text", out var text))
        {
            return text.GetString() ?? "SARIF result";
        }

        return "SARIF result";
    }

    private static SourceLocation? GetLocation(JsonElement result)
    {
        if (!result.TryGetProperty("locations", out var locations)
            || locations.GetArrayLength() == 0)
        {
            return null;
        }

        var physical = locations[0].GetProperty("physicalLocation");
        var artifact = physical.GetProperty("artifactLocation");
        var file = GetString(artifact, "uri");
        if (string.IsNullOrWhiteSpace(file))
        {
            return null;
        }

        if (Uri.TryCreate(file, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            file = uri.LocalPath;
        }

        var region = physical.TryGetProperty("region", out var regionElement)
            ? regionElement
            : default;
        var line = region.ValueKind != JsonValueKind.Undefined
                   && region.TryGetProperty("startLine", out var lineElement)
            ? lineElement.GetInt32()
            : 0;
        var column = region.ValueKind != JsonValueKind.Undefined
                     && region.TryGetProperty("startColumn", out var columnElement)
            ? columnElement.GetInt32()
            : 0;

        return new SourceLocation(file, line, column);
    }

    private static DiagnosticSeverity MapSeverity(string level)
        => level.ToLowerInvariant() switch
        {
            "error" => DiagnosticSeverity.Error,
            "note" => DiagnosticSeverity.Info,
            "none" => DiagnosticSeverity.Info,
            _ => DiagnosticSeverity.Warning
        };

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value)
            ? value.GetString()
            : null;
}
