using Proof.Core;

namespace Proof.Engine.Planning;

internal sealed class CrossProjectObligationRule : IObligationRule
{
    public string RuleId => "CrossProject";

    public void Apply(PlanningContext context)
    {
        var impact = context.Impact;
        var changedProjects = context.Index.ChangedProjects;
        var p001BProjects = context.Obligations
            .Where(item => item.RuleId == "P001B")
            .Select(item => item.SubjectId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var project in impact.ImpactedProjects.Where(project => !changedProjects.Contains(project)))
        {
            if (p001BProjects.Contains(project))
            {
                continue;
            }

            context.Obligations.Add(ObligationPlanningSupport.Create(
                "P003",
                ObligationKind.CrossProject,
                $"Project '{project}' remains build-compatible",
                project,
                required: true,
                riskWeight: 3,
                "semantic cross-project impact",
                new ProofSubject(SubjectKind.Project, project, project)));
        }
    }
}
