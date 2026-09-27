using Proof.Core;
using Proof.Engine.Binding;

namespace Proof.Engine;

internal sealed record EvidenceBindingDecision(
    string Relation,
    int Strength,
    string RuleId,
    string ReasonCode,
    string Explanation);

internal enum CapabilityCoverageKind
{
    None,
    Supporting,
    Direct,
    RequiresOverride
}

internal sealed record CapabilityCoverageDecision(
    CapabilityCoverageKind Kind,
    string? Reason = null);

internal interface IEvidenceContractRegistry
{
    EvidenceBindingDecision? MatchEvidence(ProofObligation obligation, ProofEvidence evidence);

    CapabilityCoverageDecision MatchCapability(ProofObligation obligation, EvidenceCapability capability, string? sourceDigest);
}

internal static class EvidenceKindNames
{
    internal static EvidenceKind Parse(string kind) => kind.ToLowerInvariant() switch
    {
        "build" => EvidenceKind.Build,
        "test" => EvidenceKind.TestRun,
        "analysis" => EvidenceKind.StaticAnalysis,
        "apicompatibility" or "api_compatibility" or "api-compatibility" => EvidenceKind.ApiCompatibility,
        "testmapping" or "test-mapping" => EvidenceKind.TestMapping,
        "runtimecoverage" or "runtime-coverage" or "coverage" => EvidenceKind.RuntimeCoverage,
        "manualreview" or "manual-review" => EvidenceKind.ManualReview,
        "architecture" => EvidenceKind.Architecture,
        _ => Enum.TryParse<EvidenceKind>(kind, true, out var parsed) ? parsed : EvidenceKind.ManualReview
    };

    internal static bool IsProducerOnly(string kind) => kind.ToLowerInvariant() switch
    {
        "apicompatibility" or "api_compatibility" or "api-compatibility" => true,
        "testmapping" or "test-mapping" => true,
        "runtimecoverage" or "runtime-coverage" or "coverage" => true,
        "manualreview" or "manual-review" => true,
        "architecture" => true,
        _ => false
    };
}

internal static class EvidenceCapabilityScope
{
    // scip:{name} 대상은 dotnet 명령 대상이 아니다. 범위를 다시 잡거나 커버리지를 수집할 수 없다.
    internal static bool IsScipTarget(string? commandTarget)
        => commandTarget?.StartsWith("scip:", StringComparison.OrdinalIgnoreCase) == true;
}

internal sealed class EvidenceContractRegistry : IEvidenceContractRegistry
{
    internal static EvidenceContractRegistry Shared { get; } = new();

    public EvidenceBindingDecision? MatchEvidence(ProofObligation obligation, ProofEvidence evidence)
    {
        var context = BindingMatchContext.From(evidence);
        foreach (var rule in BindingRuleRegistry.Rules)
        {
            var decision = rule.TryMatch(obligation, evidence, context);
            if (decision is null)
            {
                continue;
            }

            return new EvidenceBindingDecision(
                decision.Value.Relation,
                decision.Value.Strength,
                decision.Value.RuleId,
                decision.Value.ReasonCode,
                decision.Value.Explanation);
        }

        return null;
    }

    public CapabilityCoverageDecision MatchCapability(
        ProofObligation obligation,
        EvidenceCapability capability,
        string? sourceDigest)
    {
        // scip:{name} 대상 능력은 그 프로젝트의 주체만 낼 수 있다.
        if (EvidenceCapabilityScope.IsScipTarget(capability.CommandTarget)
            && !string.Equals(obligation.Subject?.Project, capability.CommandTarget, StringComparison.OrdinalIgnoreCase))
        {
            return new CapabilityCoverageDecision(CapabilityCoverageKind.None);
        }

        var match = MatchEvidence(obligation, DescribeProducibleEvidence(obligation, capability, sourceDigest));
        if (match is { Relation: "direct", Strength: >= 3 })
        {
            return new CapabilityCoverageDecision(CapabilityCoverageKind.Direct, match.ReasonCode);
        }

        if (match is { Relation: "supporting" })
        {
            return new CapabilityCoverageDecision(CapabilityCoverageKind.Supporting, match.ReasonCode);
        }

        return new CapabilityCoverageDecision(CapabilityCoverageKind.None);
    }

    // 능력이 낼 수 있는 모양. 실제 증거와 같은 바인딩 규칙으로 판단한다.
    // 저장소 전체 검사는 의무의 주체 참조를 담지 않으므로
    // 직접 커버리지로 칠 수 없다.
    private static ProofEvidence DescribeProducibleEvidence(
        ProofObligation obligation,
        EvidenceCapability capability,
        string? sourceDigest)
    {
        var kind = EvidenceKindNames.Parse(capability.Kind);
        var exact = capability.ScopeMode == ScopeMode.Exact;
        if (kind == EvidenceKind.TestRun
            && obligation.Kind is ObligationKind.Test or ObligationKind.CallerContract
            && exact)
        {
            kind = EvidenceKind.TestCase;
        }

        var subjectRefs = obligation.Subject is not null && (exact || kind == EvidenceKind.ApiCompatibility)
            ? new[]
            {
                new EvidenceSubjectRef(
                    obligation.Subject.Kind,
                    obligation.Subject.Id,
                    obligation.Subject.Project,
                    obligation.Subject.File,
                    obligation.Subject.DisplayName)
            }
            : null;

        var probeSubject = kind == EvidenceKind.StaticAnalysis && !exact
            ? "capability"
            : obligation.SubjectId;

        return new ProofEvidence(
            "capability",
            kind,
            probeSubject,
            EvidenceStatus.Pass,
            new EvidenceProvenance("catalog", CheckId: capability.CheckId, SourceDigest: sourceDigest),
            new EvidenceScope(
                capability.ScopeMode,
                Subjects: null,
                capability.CommandTarget,
                subjectRefs));
    }
}
