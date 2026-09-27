using System.Text.Json;
using Distill.Core.Diagnostics;
using Distill.Core.Runs;

namespace Distill.Core.Analysis;

public sealed record ApiCompatProjectResult(string Project, string Status, string? Message);

public static class ApiCompatReport
{
    public static bool TryRead(string path, out IReadOnlyList<ApiCompatProjectResult> projects, out string? error)
    {
        projects = [];
        error = null;
        if (!File.Exists(path))
        {
            error = "apicompat.json not found.";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("projects", out var projectsElement)
                || projectsElement.ValueKind != JsonValueKind.Array)
            {
                error = "apicompat.json must contain a projects array.";
                return false;
            }

            var list = new List<ApiCompatProjectResult>();
            foreach (var entry in projectsElement.EnumerateArray())
            {
                var project = entry.TryGetProperty("project", out var projectElement)
                    ? projectElement.GetString()
                    : null;
                var status = entry.TryGetProperty("status", out var statusElement)
                    ? statusElement.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(status))
                {
                    continue;
                }

                var message = entry.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString()
                    : null;
                list.Add(new ApiCompatProjectResult(project, status, message));
            }

            if (list.Count == 0)
            {
                error = "apicompat.json projects array is empty.";
                return false;
            }

            projects = list;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            error = exception.Message;
            return false;
        }
    }

    public static IReadOnlyList<DistillDiagnostic> ToDiagnostics(
        string checkId,
        IReadOnlyList<ApiCompatProjectResult> projects)
    {
        var diagnostics = new List<DistillDiagnostic>();
        foreach (var project in projects)
        {
            var severity = project.Status.ToLowerInvariant() switch
            {
                "fail" or "failed" or "breaking" => DiagnosticSeverity.Error,
                "pass" or "passed" or "unchanged" or "additive" => DiagnosticSeverity.Info,
                _ => DiagnosticSeverity.Warning
            };
            diagnostics.Add(DistillDiagnostic.Create(
                id: $"{checkId}-{project.Project}",
                kind: DiagnosticKind.Analysis,
                severity: severity,
                source: "apicompat",
                code: project.Status.ToUpperInvariant(),
                message: project.Message ?? $"API compatibility {project.Status} for {project.Project}",
                provenance: DiagnosticProvenance.RawFallback,
                confidence: 1.0,
                project: project.Project));
        }

        return diagnostics;
    }

    public static VerificationStatus ResolveStatus(IReadOnlyList<ApiCompatProjectResult> projects)
    {
        if (projects.Any(item => item.Status.Equals("fail", StringComparison.OrdinalIgnoreCase)
                                 || item.Status.Equals("failed", StringComparison.OrdinalIgnoreCase)
                                 || item.Status.Equals("breaking", StringComparison.OrdinalIgnoreCase)))
        {
            return VerificationStatus.Fail;
        }

        if (projects.Any(item => item.Status.Equals("inconclusive", StringComparison.OrdinalIgnoreCase)))
        {
            return VerificationStatus.Uncertain;
        }

        if (projects.All(item => item.Status.Equals("pass", StringComparison.OrdinalIgnoreCase)
                                 || item.Status.Equals("passed", StringComparison.OrdinalIgnoreCase)
                                 || item.Status.Equals("unchanged", StringComparison.OrdinalIgnoreCase)
                                 || item.Status.Equals("additive", StringComparison.OrdinalIgnoreCase)))
        {
            return VerificationStatus.Pass;
        }

        return VerificationStatus.Uncertain;
    }
}
