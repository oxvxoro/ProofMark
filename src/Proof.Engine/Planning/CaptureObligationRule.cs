using Proof.Core;

namespace Proof.Engine.Planning;

internal sealed class CaptureObligationRule : IObligationRule
{
    public string RuleId => "Capture";

    public void Apply(PlanningContext context)
    {
        var impact = context.Impact;
        var policy = context.Policy;
        var constraints = context.Constraints;
        var obligations = context.Obligations;

        if (impact.UntrackedCaptureFailed)
        {
            constraints.Add(ObligationPlanningSupport.Constraint(
                ProofReasonCodes.ChangeCaptureUntrackedFailed,
                "blocking",
                "Untracked file capture failed; source identity is incomplete."));
        }

        if (impact.ContentHashCaptureFailed)
        {
            constraints.Add(ObligationPlanningSupport.Constraint(
                ProofReasonCodes.ChangeCaptureContentHashFailed,
                "blocking",
                "Per-file content hash capture failed; source identity is incomplete."));
        }

        if (impact.StructuredDeltaFailed)
        {
            constraints.Add(ObligationPlanningSupport.Constraint(
                ProofReasonCodes.ChangeStructuredDeltaFailed,
                "blocking",
                "Structured git name-status/raw capture failed; change kinds cannot be trusted."));
        }

        var manualReviewSubjects = new HashSet<string>(StringComparer.Ordinal);
        foreach (var delta in impact.FileDeltas ?? [])
        {
            var rawPath = delta.NewPath ?? delta.OldPath ?? string.Empty;
            var path = rawPath.Replace('\\', '/');

            if (PathPolicy.IsManualReview(path, policy.PathRules))
            {
                ObligationPlanningSupport.AddManualReviewObligation(
                    obligations,
                    manualReviewSubjects,
                    path,
                    ProofReasonCodes.ManualReviewRequired,
                    $"Change to '{path}' is governed by a manual-review path rule.");
                continue;
            }

            switch (delta.Kind)
            {
                case FileChangeKind.Deleted:
                    if (ObligationPlanningSupport.IsResolvedDeletion(impact, delta.OldPath))
                    {
                        break;
                    }

                    ObligationPlanningSupport.AddManualReviewObligation(
                        obligations,
                        manualReviewSubjects,
                        path,
                        ProofReasonCodes.ChangeDeletionAnalysisUnavailable,
                        $"Deletion of '{delta.OldPath}' cannot be traced to callers in the base graph; a signed manual review must confirm no references remain.");
                    break;
                case FileChangeKind.BinaryModified:
                    ObligationPlanningSupport.AddManualReviewObligation(
                        obligations,
                        manualReviewSubjects,
                        path,
                        ProofReasonCodes.ChangeBinaryAnalysisUnavailable,
                        $"Binary change '{path}' has no semantic impact analysis; a signed manual review is required.");
                    break;
                case FileChangeKind.SubmoduleChanged:
                    ObligationPlanningSupport.AddManualReviewObligation(
                        obligations,
                        manualReviewSubjects,
                        path,
                        ProofReasonCodes.ChangeSubmoduleAnalysisUnavailable,
                        $"Submodule change '{path}' is unsupported; a signed manual review is required.");
                    break;
                case FileChangeKind.Unsupported:
                    constraints.Add(ObligationPlanningSupport.Constraint(
                        ProofReasonCodes.ChangeUnsupportedKind,
                        "blocking",
                        $"Unsupported change '{path}' (symlink or unknown git mode).",
                        path));
                    break;
            }
        }
    }
}
