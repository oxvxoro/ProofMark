namespace Distill.Core.Abstractions;

public interface IBuildEvidenceSource
{
    Task<Evidence.BuildEvidence> RunAsync(
        Planning.BuildCheckDefinition check,
        Runs.DistillRunContext context,
        CancellationToken cancellationToken);
}
