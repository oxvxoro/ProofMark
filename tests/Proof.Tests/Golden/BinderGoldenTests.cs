using System.Text;
using FsCheck;
using FsCheck.Xunit;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests.Golden;

/// <summary>
/// 골든 시나리오의 증거 링크를 고정한다. 바인더는
/// 결정적으로 남고 direct와 supporting 관계를 모두 유지해야 하며, 오래된
/// 소스 귀속 증거를 절대 증명으로 승격하면 안 된다.
/// </summary>
public sealed class BinderGoldenTests
{
    [Fact]
    public void Bind_GoldenScenario_IsDeterministicWithDirectSupportingAndRejected()
    {
        var plan = GoldenScenarioFactory.Plan();
        var evidence = GoldenScenarioFactory.Evidence();

        var first = new EvidenceBinder().Bind(plan, evidence);
        var second = new EvidenceBinder().Bind(plan, evidence);

        Assert.Equal(Canonicalize(first.Links), Canonicalize(second.Links));
        Assert.Contains(first.Links, link => link.Relation == "direct");
        Assert.Contains(first.Links, link => link.Relation == "supporting");
        Assert.Contains(
            first.Links,
            link => link.Relation == "rejected"
                && link.ReasonCode == ProofReasonCodes.EvidenceStaleSource);
        Assert.DoesNotContain(first.Links, link => link.EvidenceId == "E-stale" && link.Relation != "rejected");
    }

    [Property]
    public bool StaleSourceBoundEvidence_IsNeverDirect(NonEmptyString staleDigest)
    {
        var plan = TestPlan();
        var evidence = new ProofEvidence(
            "E1",
            EvidenceKind.TestCase,
            "sym",
            EvidenceStatus.Pass,
            new EvidenceProvenance("distill", CheckId: "unit", SourceDigest: "stale-" + staleDigest.Get),
            new EvidenceScope(
                ScopeMode.Exact,
                ["sym"],
                null,
                [new EvidenceSubjectRef(SubjectKind.Test, "sym", FullyQualifiedName: "App.Tests.T.M")]));

        var links = new EvidenceBinder().Bind(plan, [evidence]).Links;
        return links.Count > 0 && links.All(link => link.Relation == "rejected");
    }

    [Property]
    public bool UnrelatedEvidence_DoesNotWeakenDirectProof(PositiveInt seed)
    {
        var plan = TestPlan();
        var direct = new ProofEvidence(
            "E1",
            EvidenceKind.TestCase,
            "sym",
            EvidenceStatus.Pass,
            new EvidenceProvenance("distill", CheckId: "unit", SourceDigest: "plan-digest"),
            new EvidenceScope(
                ScopeMode.Exact,
                ["sym"],
                null,
                [new EvidenceSubjectRef(SubjectKind.Test, "sym", FullyQualifiedName: "App.Tests.T.M")]));

        var noise = Enumerable.Range(0, 1 + (seed.Get % 5))
            .Select(index => new ProofEvidence(
                $"N{index}",
                EvidenceKind.TestCase,
                $"noise-{index}",
                EvidenceStatus.Pass,
                new EvidenceProvenance("distill", CheckId: "unit", SourceDigest: "plan-digest"),
                new EvidenceScope(ScopeMode.Contains, [$"noise-{index}"])))
            .ToArray();

        var baseline = new EvidenceBinder().Bind(plan, [direct]);
        var withNoise = new EvidenceBinder().Bind(plan, [direct, .. noise]);
        var baselineDirect = DirectEvidenceIds(baseline.Links);
        var noisyDirect = DirectEvidenceIds(withNoise.Links);
        return baselineDirect.Contains("E1") && noisyDirect.Contains("E1");
    }

    private static ProofPlan TestPlan() => new(
        [
            new ProofObligation(
                "O1",
                "P004",
                ObligationKind.Test,
                "claim",
                "sym",
                true,
                4,
                ["r"],
                new ProofSubject(SubjectKind.Test, "sym", DisplayName: "App.Tests.T.M"))
        ],
        SourceDigest: "plan-digest");

    private static HashSet<string> DirectEvidenceIds(IReadOnlyList<ObligationEvidenceLink> links)
        => links
            .Where(link => link.ObligationId == "O1" && link.Relation == "direct")
            .Select(link => link.EvidenceId)
            .ToHashSet(StringComparer.Ordinal);

    private static string Canonicalize(IReadOnlyList<ObligationEvidenceLink> links)
    {
        var builder = new StringBuilder();
        foreach (var link in links)
        {
            builder.Append(link.ObligationId).Append('|')
                .Append(link.EvidenceId).Append('|')
                .Append(link.Relation).Append('|')
                .Append(link.Strength).Append('|')
                .Append(link.BindingRuleId).Append('|')
                .Append(link.ReasonCode).Append('|')
                .Append(link.Explanation)
                .AppendLine();
        }

        return builder.ToString();
    }
}
