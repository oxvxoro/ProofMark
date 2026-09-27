namespace Distill.Git;

public sealed record ChangedHunk(
    string File,
    int OldStart,
    int OldLength,
    int NewStart,
    int NewLength,
    string Patch);
