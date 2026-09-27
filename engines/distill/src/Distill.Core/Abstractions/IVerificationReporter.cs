using Distill.Core.Diagnostics;
using Distill.Core.Planning;
using Distill.Core.Runs;

namespace Distill.Core.Abstractions;

public enum ReportOutputFormat
{
    Compact,
    Json
}

public sealed record ReportOptions(
    ReportOutputFormat Format = ReportOutputFormat.Compact,
    int MaxDiagnostics = 8,
    IReadOnlyList<string>? RedactionPatterns = null,
    double MinConfidence = 0.5);

public sealed record VerificationPack(
    VerificationStatus Status,
    string CompactText,
    string? JsonText,
    IReadOnlyList<DistillDiagnostic> Diagnostics,
    IReadOnlyList<CheckRunResult> Checks,
    string RunDirectory);

public interface IVerificationReporter
{
    VerificationPack Build(VerificationRun run, ReportOptions options);
}
