namespace Distill.Core.Evidence;

public sealed record TestCaseEvidence(
    string Name,
    string Outcome,
    string? Message,
    string? Stack,
    double DurationMs,
    string? FullyQualifiedName = null,
    string? Project = null);
