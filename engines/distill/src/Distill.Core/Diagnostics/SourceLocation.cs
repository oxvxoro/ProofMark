namespace Distill.Core.Diagnostics;

public sealed record SourceLocation(
    string File,
    int Line,
    int Column);
