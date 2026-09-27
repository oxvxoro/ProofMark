using Distill.Core.Diagnostics;
using Distill.Core.Planning;
using Distill.Core.Runs;

namespace Distill.Core.Runs;

public sealed class VerificationRun
{
    public required string RunId { get; init; }

    public required string Profile { get; init; }

    public required VerificationStatus Status { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset FinishedAt { get; init; }

    public IReadOnlyList<CheckRunResult> Checks { get; init; } = Array.Empty<CheckRunResult>();

    public IReadOnlyList<DistillDiagnostic> Diagnostics { get; init; } = Array.Empty<DistillDiagnostic>();

    public IReadOnlyList<Evidence.BuildEvidence> BuildResults { get; init; } = Array.Empty<Evidence.BuildEvidence>();
}
