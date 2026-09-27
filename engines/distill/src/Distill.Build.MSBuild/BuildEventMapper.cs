using Distill.Core.Diagnostics;
using Microsoft.Build.Framework;

namespace Distill.Build.MSBuild;

internal static class BuildEventMapper
{
    public static DistillDiagnostic MapError(BuildErrorEventArgs e, int index)
    {
        SourceLocation? location = null;
        if (!string.IsNullOrWhiteSpace(e.File))
        {
            location = new SourceLocation(
                NormalizePath(e.File),
                e.LineNumber,
                e.ColumnNumber);
        }

        return DistillDiagnostic.Create(
            id: $"build-error-{index + 1}",
            kind: DiagnosticKind.Build,
            severity: DiagnosticSeverity.Error,
            source: "msbuild",
            code: string.IsNullOrWhiteSpace(e.Code) ? null : e.Code,
            message: e.Message ?? string.Empty,
            location: location,
            project: string.IsNullOrWhiteSpace(e.ProjectFile) ? null : NormalizePath(e.ProjectFile),
            provenance: DiagnosticProvenance.MsBuildBinaryLog,
            confidence: 1.0);
    }

    public static DistillDiagnostic MapWarning(BuildWarningEventArgs e, int index)
    {
        SourceLocation? location = null;
        if (!string.IsNullOrWhiteSpace(e.File))
        {
            location = new SourceLocation(
                NormalizePath(e.File),
                e.LineNumber,
                e.ColumnNumber);
        }

        return DistillDiagnostic.Create(
            id: $"build-warning-{index + 1}",
            kind: DiagnosticKind.Build,
            severity: DiagnosticSeverity.Warning,
            source: "msbuild",
            code: string.IsNullOrWhiteSpace(e.Code) ? null : e.Code,
            message: e.Message ?? string.Empty,
            location: location,
            project: string.IsNullOrWhiteSpace(e.ProjectFile) ? null : NormalizePath(e.ProjectFile),
            provenance: DiagnosticProvenance.MsBuildBinaryLog,
            confidence: 1.0);
    }

    private static string NormalizePath(string path)
        => path.Replace('\\', '/');
}
