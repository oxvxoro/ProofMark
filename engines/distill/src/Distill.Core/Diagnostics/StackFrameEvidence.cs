namespace Distill.Core.Diagnostics;

public sealed record StackFrameEvidence(
    string? File,
    int Line,
    string? Method,
    bool IsFramework);
