using Proof.Core;

namespace Proof.Engine.Planning;

internal sealed class PublicApiObligationRule : IObligationRule
{
    public string RuleId => "PublicApi";

    public void Apply(PlanningContext context)
    {
        var impact = context.Impact;
        var policy = context.Policy;
        var publicSymbols = impact.ChangedSymbols.Where(symbol => symbol.IsPublic).ToArray();
        foreach (var symbol in publicSymbols)
        {
            context.Obligations.Add(ObligationPlanningSupport.Create(
                "P001A",
                ObligationKind.Compatibility,
                $"Public API surface of '{symbol.DisplayName}' remains compatible",
                symbol.Id,
                policy.PublicApiCompatibilityRequired,
                riskWeight: 4,
                $"changed public symbol in {symbol.Project}",
                new ProofSubject(SubjectKind.ApiSurface, symbol.Id, symbol.Project, symbol.File, symbol.DisplayName)));
        }

        if (publicSymbols.Length > 0)
        {
            foreach (var project in impact.ImpactedProjects.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                context.Obligations.Add(ObligationPlanningSupport.Create(
                    "P001B",
                    ObligationKind.Build,
                    $"Internal consumer project '{project}' compiles after public API change",
                    project,
                    policy.InternalConsumerCompatibilityRequired,
                    riskWeight: 3,
                    "impacted internal consumer must compile",
                    new ProofSubject(SubjectKind.Project, project, project)));
            }
        }
    }
}
