using Proof.Core;
using Proof.Engine.Planning.Specifications;

namespace Proof.Engine.Planning;

internal sealed class ArchitectureObligationRule : IObligationRule
{
    private static readonly ArchitectureEnabledSpecification ArchitectureEnabled = new();

    public string RuleId => "Architecture";

    public void Apply(PlanningContext context)
    {
        var impact = context.Impact;
        var policy = context.Policy;
        if (!ArchitectureEnabled.Evaluate(policy).IsSatisfied)
        {
            return;
        }

        var architectureRequired = policy.Architecture == ArchitecturePolicyMode.Required;
        if (architectureRequired)
        {
            context.Obligations.Add(ObligationPlanningSupport.Create(
                "P011",
                ObligationKind.Architecture,
                "Architecture rules exist and the completed CodeMap analysis has no cycle or layer violations",
                "architecture-rules",
                required: true,
                riskWeight: 3,
                impact.ArchitectureRulesPresent != true
                    ? "architecture rules file missing"
                    : impact.ArchitectureViolations is null
                        ? "architecture check did not run"
                        : "architecture policy check required",
                new ProofSubject(SubjectKind.Repository, "architecture-rules", DisplayName: "architecture-rules")));

            if (impact.ArchitectureViolations is null)
            {
                return;
            }
        }

        foreach (var group in (impact.ArchitectureViolations ?? [])
                      .GroupBy(item => item.SubjectId, StringComparer.Ordinal))
        {
            var orderedViolations = group
                .OrderBy(item => item.Kind, StringComparer.Ordinal)
                .ThenBy(item => item.Project, StringComparer.Ordinal)
                .ThenBy(item => item.File, StringComparer.Ordinal)
                .ThenBy(item => item.DisplayName, StringComparer.Ordinal)
                .ThenBy(item => item.Message, StringComparer.Ordinal)
                .ToArray();
            var kinds = orderedViolations.Select(item => item.Kind)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(kind => kind, StringComparer.Ordinal)
                .ToArray();
            var kindsKey = string.Join(',', kinds);
            var required = policy.Architecture == ArchitecturePolicyMode.Required
                && kinds.Any(kind => kind is "cycle" or "layer");
            var first = orderedViolations[0];
            var subject = first.Kind == "cycle"
                ? new ProofSubject(SubjectKind.Project, group.Key, first.Project, first.File, first.DisplayName)
                : new ProofSubject(SubjectKind.Symbol, group.Key, first.Project, first.File, first.DisplayName);
            context.Obligations.Add(new ProofObligation(
                ObligationPlanningSupport.StableId("O", "P011", ObligationKind.Architecture.ToString(), group.Key),
                "P011",
                ObligationKind.Architecture,
                $"Architecture rule '{kindsKey}' satisfied for '{group.Key}'",
                group.Key,
                required,
                required ? 3 : 1,
                kinds,
                subject));
        }
    }
}
