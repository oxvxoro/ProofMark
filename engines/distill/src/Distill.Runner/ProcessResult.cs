namespace Distill.Runner;

public sealed record ProcessResult(
    int? ExitCode,
    TimeSpan Duration,
    ProcessStatus Status,
    string? StdoutPath,
    string? StderrPath,
    string? ErrorMessage = null);
