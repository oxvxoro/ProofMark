using Proof.Core;

namespace Proof.Engine.Planning;

internal static class ObligationPlanningSupport
{
    internal static bool PathsAlign(string deletedPath, string resolvedPath)
    {
        if (string.IsNullOrWhiteSpace(deletedPath) || string.IsNullOrWhiteSpace(resolvedPath))
        {
            return false;
        }

        if (string.Equals(deletedPath, resolvedPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!resolvedPath.Contains('/', StringComparison.Ordinal))
        {
            return false;
        }

        return deletedPath.EndsWith("/" + resolvedPath, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsResolvedDeletion(ChangeImpact impact, string? oldPath)
    {
        if (impact.DeletionPathsResolved is not { Count: > 0 } resolved
            || string.IsNullOrWhiteSpace(oldPath))
        {
            return false;
        }

        var normalized = oldPath.Replace('\\', '/').TrimStart('/');
        foreach (var candidate in resolved)
        {
            if (PathsAlign(normalized, candidate.Replace('\\', '/').TrimStart('/')))
            {
                return true;
            }
        }

        return false;
    }

    internal static ProofObligation Create(
        string ruleId,
        ObligationKind kind,
        string claim,
        string subjectId,
        bool required,
        int riskWeight,
        string reason,
        ProofSubject? subject = null)
        => new(
            StableId("O", ruleId, kind.ToString(), subjectId),
            ruleId,
            kind,
            claim,
            subjectId,
            required,
            riskWeight,
            [reason],
            subject);

    internal static AnalysisConstraint Constraint(string code, string severity, string message, string? subject = null)
        => new(StableId("C", code, subject ?? string.Empty, message), code, severity, message, subject);

    internal static string StableId(string prefix, params string[] parts)
    {
        var key = string.Join('|', parts);
        var hash = Sha256Hex.HashText(key);
        return $"{prefix}-{parts[0]}-{hash[..8]}";
    }

    internal static void AddManualReviewObligation(
        List<ProofObligation> obligations,
        HashSet<string> seenSubjects,
        string subjectId,
        string reasonCode,
        string claim)
    {
        if (string.IsNullOrWhiteSpace(subjectId) || !seenSubjects.Add(subjectId))
        {
            return;
        }

        obligations.Add(Create(
            "P009",
            ObligationKind.ManualReview,
            claim,
            subjectId,
            required: true,
            riskWeight: 3,
            reasonCode,
            new ProofSubject(SubjectKind.File, subjectId, File: subjectId, DisplayName: subjectId)));
    }

    internal static void AddConstraintOrObligation(
        List<AnalysisConstraint> constraints,
        List<ProofObligation> obligations,
        string code,
        string message,
        UncertaintyDisposition disposition)
    {
        var severity = disposition == UncertaintyDisposition.Blocking ? "blocking" : "advisory";
        constraints.Add(Constraint(code, severity, message));
        if (disposition == UncertaintyDisposition.Blocking)
        {
            obligations.Add(Create(
                code == ProofReasonCodes.HeuristicEdgeBlocking ? "P007" : "P006",
                ObligationKind.Uncertainty,
                message,
                code,
                required: true,
                riskWeight: 1,
                code,
                new ProofSubject(SubjectKind.Repository, code, DisplayName: message)));
        }
    }
}
