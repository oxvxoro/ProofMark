using Proof.Core;

namespace Proof.Engine;

public sealed class DeterministicProofEvaluator : IProofEvaluator
{
    public ProofEvaluation Evaluate(ProofPlan plan, VerificationEvidenceSet evidence)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(evidence);

        var evidenceById = evidence.Evidence.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var linksByObligation = evidence.Links
            .GroupBy(link => link.ObligationId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var evaluated = new List<EvaluatedObligation>();
        foreach (var obligation in plan.Obligations)
        {
            var status = ResolveStatus(obligation, linksByObligation, evidenceById);
            var evidenceIds = linksByObligation.TryGetValue(obligation.Id, out var links)
                ? links.Select(link => link.EvidenceId).Distinct(StringComparer.Ordinal).ToArray()
                : Array.Empty<string>();
            evaluated.Add(new EvaluatedObligation(obligation, status, evidenceIds));
        }

        var verdict = ResolveVerdict(evaluated, evidence.Evidence, plan);
        var reason = verdict == ProofVerdict.NoChange
            ? ProofReasonCodes.NoChange
            : verdict == ProofVerdict.Uncertain && plan is { ChangeSetIsEmpty: false, Obligations.Count: 0 }
                ? ProofReasonCodes.ChangeNonemptyPlanEmpty
                : null;
        return new ProofEvaluation(verdict, evaluated, reason);
    }

    internal static ObligationStatus ResolveStatus(
        ProofObligation obligation,
        IReadOnlyDictionary<string, ObligationEvidenceLink[]> linksByObligation,
        IReadOnlyDictionary<string, ProofEvidence> evidenceById)
    {
        if (obligation.Kind == ObligationKind.Uncertainty)
        {
            return ObligationStatus.Unresolved;
        }

        if (!linksByObligation.TryGetValue(obligation.Id, out var links) || links.Length == 0)
        {
            return ObligationStatus.Unresolved;
        }

        var admitted = links
            .Select(link => (Link: link, Evidence: evidenceById.GetValueOrDefault(link.EvidenceId)))
            .Where(item => item.Evidence is not null)
            .ToArray();

        var direct = admitted
            .Where(item => IsDirect(item.Link) && item.Link.Strength >= 3)
            .ToArray();

        if (direct.Any(item => IsFailed(item.Evidence!.Status)))
        {
            return ObligationStatus.Failed;
        }

        if (direct.Any(item => IsInfraError(item.Evidence!.Status)))
        {
            return ObligationStatus.Blocked;
        }

        if (direct.Any(item => IsPass(item.Evidence!.Status)))
        {
            return ObligationStatus.Proven;
        }

        return ObligationStatus.Unresolved;
    }

    internal static ProofVerdict ResolveVerdict(
        IReadOnlyList<EvaluatedObligation> obligations,
        IReadOnlyList<ProofEvidence> evidence,
        ProofPlan plan)
    {
        if (plan.ChangeSetIsEmpty)
        {
            return ProofVerdict.NoChange;
        }

        var required = obligations.Where(item => item.Obligation.Required).ToArray();
        if (required.Any(item => item.Status == ObligationStatus.Failed))
        {
            return ProofVerdict.NotReady;
        }

        var relevantInfra = required.Any(item => item.Status == ObligationStatus.Blocked)
            || evidence.Any(item => IsInfraError(item.Status) && IsRelevantInfra(item, plan));
        if (relevantInfra)
        {
            return ProofVerdict.InfraError;
        }

        var blockingConstraints = (plan.Constraints ?? Array.Empty<AnalysisConstraint>())
            .Where(constraint => string.Equals(constraint.Severity, "blocking", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (blockingConstraints.Length > 0)
        {
            return ProofVerdict.Uncertain;
        }

        if (required.Length == 0)
        {
            return ProofVerdict.Uncertain;
        }

        if (required.Any(item => item.Status is ObligationStatus.Unresolved or ObligationStatus.Blocked or ObligationStatus.Pending))
        {
            return ProofVerdict.Uncertain;
        }

        if (required.All(item => item.Status == ObligationStatus.Proven))
        {
            return ProofVerdict.Proven;
        }

        return ProofVerdict.Uncertain;
    }

    private static bool IsRelevantInfra(ProofEvidence evidence, ProofPlan plan)
    {
        if (plan.Obligations.Count == 0)
        {
            return true;
        }

        return plan.Obligations.Any(obligation =>
            string.Equals(evidence.Provenance.CheckId, obligation.SubjectId, StringComparison.OrdinalIgnoreCase)
            || (evidence.Scope?.CommandTarget is not null
                && obligation.Subject?.Project is not null
                && evidence.Scope.CommandTarget.Contains(obligation.Subject.Project, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsDirect(ObligationEvidenceLink link)
        => string.Equals(link.Relation, "direct", StringComparison.OrdinalIgnoreCase);

    private static bool IsPass(EvidenceStatus status)
        => status == EvidenceStatus.Pass;

    private static bool IsFailed(EvidenceStatus status)
        => status == EvidenceStatus.Fail;

    private static bool IsInfraError(EvidenceStatus status)
        => status == EvidenceStatus.InfraError;
}
