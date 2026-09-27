using Proof.Core;
using Proof.Engine.Planning.Specifications;

namespace Proof.Engine.Planning;

internal sealed class AppContractObligationRule : IObligationRule
{
    internal static readonly IReadOnlyList<string> AppEdgeKinds =
    [
        "RoutesTo", "Registers", "ResolvesTo", "Renders", "BindsTo", "HandlesEvent", "UsesViewModel"
    ];

    public string RuleId => "AppContract";

    public void Apply(PlanningContext context)
    {
        var impact = context.Impact;
        var policy = context.Policy;
        if (!policy.AppContractRequired)
        {
            return;
        }

        var appRelation = new SemanticAppRelationSpecification(policy);
        var seenSubjects = new HashSet<string>(StringComparer.Ordinal);
        var index = context.Index;
        foreach (var relation in impact.Relations ?? [])
        {
            if (!appRelation.Evaluate(relation).IsSatisfied)
            {
                continue;
            }

            index.ImpactedById.TryGetValue(relation.ImpactedSymbolId, out var impacted);
            if (impacted is { IsTest: true })
            {
                continue;
            }

            if (!seenSubjects.Add(relation.ImpactedSymbolId))
            {
                continue;
            }

            index.ChangedById.TryGetValue(relation.RootChangedSymbolId, out var changed);
            context.Obligations.Add(ObligationPlanningSupport.Create(
                "P010",
                ObligationKind.AppContract,
                $"App contract for '{impacted?.DisplayName ?? relation.ImpactedSymbolId}' remains valid ({relation.EdgeKind})",
                relation.ImpactedSymbolId,
                required: true,
                riskWeight: 2,
                "app-graph edge under analysis.impact.profile=app",
                new ProofSubject(
                    SubjectKind.Symbol,
                    relation.ImpactedSymbolId,
                    impacted?.Project ?? changed?.Project,
                    impacted?.File ?? changed?.File,
                    impacted?.DisplayName ?? relation.ImpactedSymbolId)));
        }
    }
}
