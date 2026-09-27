using System.Text;
using FsCheck;
using FsCheck.Xunit;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests.Golden;

/// <summary>
/// 골든 시나리오의 계획 형태와 순서 불변식을 고정한다. 플래너는
/// 입력 심볼 순서와 관계없이 같은 의무를 만들어야 하고,
/// 인증서 페이로드가 의존하는 정규 순서로 그것을 내보내야 한다.
/// </summary>
public sealed class PlannerGoldenTests
{
    [Fact]
    public void Plan_GoldenScenario_CoversEveryPlannedRuleAndIsDeterministic()
    {
        var first = Canonicalize(GoldenScenarioFactory.Plan());
        var second = Canonicalize(GoldenScenarioFactory.Plan());
        Assert.Equal(first, second);

        var ruleIds = GoldenScenarioFactory.Plan()
            .Obligations
            .Select(obligation => obligation.RuleId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var expected in new[] { "P001A", "P001B", "P002", "P004", "P005", "P008", "P009", "P010", "P011" })
        {
            Assert.Contains(expected, ruleIds);
        }
    }

    [Fact]
    public void Plan_GoldenScenario_RespectsCanonicalOrdering()
    {
        var obligations = GoldenScenarioFactory.Plan().Obligations;
        for (var index = 1; index < obligations.Count; index++)
        {
            Assert.True(
                Compare(obligations[index - 1], obligations[index]) <= 0,
                $"obligation {index} is out of canonical order");
        }
    }

    [Property]
    public bool ChangedSymbolOrder_DoesNotChangePlan(PositiveInt seed)
    {
        var impact = GoldenScenarioFactory.Impact();
        var shuffled = GoldenScenarioFactory.WithChangedSymbolOrder(impact, seed.Get);
        return Canonicalize(new DeterministicProofPlanner().Plan(impact, GoldenScenarioFactory.Policy()))
            == Canonicalize(new DeterministicProofPlanner().Plan(shuffled, GoldenScenarioFactory.Policy()));
    }

    [Property]
    public bool ImpactedSymbolOrder_DoesNotChangePlan(PositiveInt seed)
    {
        var impact = GoldenScenarioFactory.Impact();
        var shuffled = GoldenScenarioFactory.WithImpactedSymbolOrder(impact, seed.Get);
        return Canonicalize(new DeterministicProofPlanner().Plan(impact, GoldenScenarioFactory.Policy()))
            == Canonicalize(new DeterministicProofPlanner().Plan(shuffled, GoldenScenarioFactory.Policy()));
    }

    private static int Compare(ProofObligation left, ProofObligation right)
    {
        var comparison = right.Required.CompareTo(left.Required);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = string.CompareOrdinal(left.RuleId, right.RuleId);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = string.CompareOrdinal(
            left.Subject?.Kind.ToString() ?? string.Empty,
            right.Subject?.Kind.ToString() ?? string.Empty);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = string.CompareOrdinal(left.SubjectId, right.SubjectId);
        return comparison != 0 ? comparison : string.CompareOrdinal(left.Claim, right.Claim);
    }

    private static string Canonicalize(ProofPlan plan)
    {
        var builder = new StringBuilder();
        foreach (var obligation in plan.Obligations)
        {
            builder.Append(obligation.Id).Append('|')
                .Append(obligation.RuleId).Append('|')
                .Append(obligation.Kind).Append('|')
                .Append(obligation.SubjectId).Append('|')
                .Append(obligation.Required).Append('|')
                .Append(obligation.RiskWeight).Append('|')
                .Append(string.Join(',', obligation.Reasons))
                .AppendLine();
        }

        foreach (var constraint in plan.Constraints ?? [])
        {
            builder.Append("C|").Append(constraint.Id).Append('|')
                .Append(constraint.Code).Append('|')
                .Append(constraint.Severity).Append('|')
                .Append(constraint.Subject).Append('|')
                .Append(constraint.Message)
                .AppendLine();
        }

        return builder.ToString();
    }
}
