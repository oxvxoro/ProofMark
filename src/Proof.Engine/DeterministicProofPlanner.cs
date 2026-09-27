using Proof.Core;
using Proof.Engine.Determinism;
using Proof.Engine.Planning;

namespace Proof.Engine;

public sealed class DeterministicProofPlanner : IProofPlanner
{
    public ProofPlan Plan(ChangeImpact impact, ProofPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(impact);
        policy ??= new ProofPolicy();

        var callers = impact.Callers is { Count: > 0 }
            ? impact.Callers
            : impact.CallerSubjectIds
                .Select(id => new CallerRelation(string.Empty, id, string.Empty, null, "Calls", null, IsTest: false))
                .ToArray();
        var relations = impact.Relations is { Count: > 0 }
            ? impact.Relations
            : impact.ImpactedSymbols
                .Select(item => new ImpactRelation(
                    item.RootChangedSymbolId,
                    item.Id,
                    item.Depth,
                    "Impact",
                    item.ResolutionKind,
                    item.Confidence))
                .ToArray();

        var context = new PlanningContext
        {
            Impact = impact,
            Policy = policy,
            Callers = callers,
            Relations = relations,
            Index = new PlanningIndex(impact, callers, relations),
            Constraints = [.. impact.Constraints ?? []],
        };

        foreach (var rule in ObligationRuleRegistry.Rules)
        {
            rule.Apply(context);
        }

        var obligations = ProofOrdering.Obligations(context.Obligations).ToList();
        var constraints = ProofOrdering.Constraints(context.Constraints).ToList();

        var empty = impact.ChangeSetIsEmptyExplicit()
            || (impact.Spans.Count == 0 && impact.ChangedSymbols.Count == 0 && (impact.FileDeltas is null || impact.FileDeltas.Count == 0));

        return new ProofPlan(
            obligations,
            constraints,
            impact.SourceDigest,
            empty);
    }

    internal static bool PathsAlign(string deletedPath, string resolvedPath)
        => ObligationPlanningSupport.PathsAlign(deletedPath, resolvedPath);

    internal static string StableId(string prefix, params string[] parts)
        => ObligationPlanningSupport.StableId(prefix, parts);
}
