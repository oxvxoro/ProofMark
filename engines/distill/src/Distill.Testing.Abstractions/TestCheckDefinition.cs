namespace Distill.Testing.Abstractions;

public sealed record TestCheckDefinition(
    string Id,
    string Target,
    IReadOnlyList<string> Arguments,
    string? SourceHint = null,
    TimeSpan? Timeout = null);
