namespace Distill.Core.Runs;

public sealed class DistillRunContext
{
    public required string RunId { get; init; }

    public required string WorkspaceRoot { get; init; }

    public required string RunDirectory { get; init; }

    public string? Profile { get; init; }
}
