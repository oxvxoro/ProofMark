using Proof.Core;

namespace Proof.Engine.Planning;

internal sealed class CallerContractObligationRule : IObligationRule
{
    public string RuleId => "CallerContract";

    public void Apply(PlanningContext context)
    {
        var index = context.Index;

        foreach (var caller in context.Callers.Where(item => item.IsTest).DistinctBy(item => item.CallerSymbolId, StringComparer.Ordinal))
        {
            index.ChangedById.TryGetValue(caller.CallerSymbolId, out var changed);
            index.ImpactedById.TryGetValue(caller.CallerSymbolId, out var impacted);
            var display = changed?.DisplayName ?? impacted?.DisplayName ?? caller.CallerSymbolId;
            context.Obligations.Add(ObligationPlanningSupport.Create(
                "P002",
                ObligationKind.CallerContract,
                $"Caller contract for '{display}' remains valid",
                caller.CallerSymbolId,
                required: true,
                riskWeight: 3,
                "CodeMap detected caller relation",
                new ProofSubject(
                    SubjectKind.Symbol,
                    caller.CallerSymbolId,
                    string.IsNullOrWhiteSpace(caller.CallerProject) ? impacted?.Project ?? changed?.Project : caller.CallerProject,
                    caller.File,
                    display)));
        }
    }
}
