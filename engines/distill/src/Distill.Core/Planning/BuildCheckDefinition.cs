namespace Distill.Core.Planning;

public sealed record BuildCheckDefinition(
    string Id,
    string Target,
    IReadOnlyList<string> Arguments,
    TimeSpan? Timeout = null);
