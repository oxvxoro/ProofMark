namespace Distill.Core.Diagnostics;

public sealed record DistillDiagnostic(
    string Id,
    DiagnosticKind Kind,
    DiagnosticSeverity Severity,
    string Source,
    string? Code,
    string Message,
    SourceLocation? Location,
    string? Project,
    string? TestName,
    ExceptionEvidence? Exception,
    IReadOnlyList<StackFrameEvidence> Frames,
    DiagnosticProvenance Provenance,
    double Confidence,
    IReadOnlyDictionary<string, string>? Properties)
{
    public static DistillDiagnostic Create(
        string id,
        DiagnosticKind kind,
        DiagnosticSeverity severity,
        string source,
        string? code,
        string message,
        SourceLocation? location = null,
        string? project = null,
        string? testName = null,
        ExceptionEvidence? exception = null,
        IReadOnlyList<StackFrameEvidence>? frames = null,
        DiagnosticProvenance provenance = DiagnosticProvenance.RawFallback,
        double confidence = 1.0,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        if (confidence is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "Confidence must be between 0.0 and 1.0.");
        }

        return new DistillDiagnostic(
            id,
            kind,
            severity,
            source,
            code,
            message,
            location,
            project,
            testName,
            exception,
            frames ?? Array.Empty<StackFrameEvidence>(),
            provenance,
            confidence,
            properties);
    }
}
