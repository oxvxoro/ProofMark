using Proof.Core;
using Proof.Engine.Binding;

namespace Proof.Engine;

public sealed class EvidenceBinder : IEvidenceBinder
{
    public VerificationEvidenceSet Bind(
        ProofPlan plan,
        IReadOnlyList<ProofEvidence> evidence,
        VerificationPlan? verificationPlan = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(evidence);

        var index = new EvidenceIndex(Admit(evidence, verificationPlan), plan.SourceDigest);
        var links = new List<ObligationEvidenceLink>();

        foreach (var obligation in plan.Obligations)
        {
            if (obligation.Kind == ObligationKind.Uncertainty)
            {
                continue;
            }

            foreach (var candidate in index.CandidatesFor(obligation))
            {
                var item = candidate.Evidence;

                if (candidate.State == SourceBindingState.MissingDigest)
                {
                    links.Add(new ObligationEvidenceLink(
                        obligation.Id,
                        item.Id,
                        "rejected",
                        0,
                        "BIND_SOURCE_DIGEST",
                        ProofReasonCodes.EvidenceSourceIdentityMissing,
                        "Evidence is missing a source digest while the plan requires one."));
                    continue;
                }

                if (candidate.State == SourceBindingState.StaleDigest)
                {
                    links.Add(new ObligationEvidenceLink(
                        obligation.Id,
                        item.Id,
                        "rejected",
                        0,
                        "BIND_SOURCE_DIGEST",
                        ProofReasonCodes.EvidenceStaleSource,
                        "Evidence source digest does not match the planned snapshot."));
                    continue;
                }

                var decision = Match(obligation, item);
                if (decision is null)
                {
                    continue;
                }

                links.Add(new ObligationEvidenceLink(
                    obligation.Id,
                    item.Id,
                    decision.Value.Relation,
                    decision.Value.Strength,
                    decision.Value.RuleId,
                    decision.Value.ReasonCode,
                    decision.Value.Explanation));
            }
        }

        return new VerificationEvidenceSet(evidence, links);
    }

    private static IReadOnlyList<ProofEvidence> Admit(
        IReadOnlyList<ProofEvidence> evidence,
        VerificationPlan? verificationPlan)
    {
        if (verificationPlan is null)
        {
            return evidence;
        }

        var allowedChecks = verificationPlan.Checks
            .Select(item => item.CheckId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return evidence
            .Where(item => !string.IsNullOrWhiteSpace(item.Provenance.CheckId)
                && allowedChecks.Contains(item.Provenance.CheckId))
            .ToArray();
    }

    public static (string Relation, int Strength, string RuleId, string ReasonCode, string Explanation)? Match(
        ProofObligation obligation,
        ProofEvidence evidence)
    {
        var decision = EvidenceContractRegistry.Shared.MatchEvidence(obligation, evidence);
        return decision is null
            ? null
            : (decision.Relation, decision.Strength, decision.RuleId, decision.ReasonCode, decision.Explanation);
    }

    internal static bool TestIdentityMatches(ProofEvidence evidence, ProofObligation obligation, bool exact)
        => BindingIdentity.TestIdentityMatches(evidence, obligation, exact);
}
