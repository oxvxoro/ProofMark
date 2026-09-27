using Proof.Core;

namespace Proof.Engine.Planning;

internal sealed class StaticAnalysisObligationRule : IObligationRule
{
    public string RuleId => "StaticAnalysis";

    public void Apply(PlanningContext context)
    {
        var impact = context.Impact;
        var policy = context.Policy;
        if (!policy.StaticAnalysisRequired)
        {
            return;
        }

        foreach (var project in impact.ImpactedProjects
                     .Concat(impact.ChangedSymbols.Select(symbol => symbol.Project))
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(value => value, StringComparer.Ordinal))
        {
            context.Obligations.Add(ObligationPlanningSupport.Create(
                "P008",
                ObligationKind.StaticAnalysis,
                $"Static analysis passes for project '{project}'",
                project,
                required: true,
                riskWeight: 2,
                "policy.staticAnalysis=required",
                new ProofSubject(SubjectKind.Project, project, project)));
        }
    }
}
