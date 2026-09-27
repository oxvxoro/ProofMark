using Proof.Core;

namespace Proof.Engine;

public sealed class VerificationPlanner : IVerificationPlanner
{
    private readonly IEvidenceContractRegistry _contracts;

    public VerificationPlanner()
        : this(EvidenceContractRegistry.Shared)
    {
    }

    internal VerificationPlanner(IEvidenceContractRegistry contracts)
    {
        _contracts = contracts;
    }

    public VerificationPlan Plan(
        ProofPlan proofPlan,
        IReadOnlyList<EvidenceCapability> catalog,
        string profile)
    {
        ArgumentNullException.ThrowIfNull(proofPlan);
        ArgumentNullException.ThrowIfNull(catalog);

        var uncovered = new List<UncoveredObligation>();
        var covers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var exactCovered = new HashSet<string>(StringComparer.Ordinal);
        var overrideCovers = new List<OverrideCover>();
        foreach (var capability in catalog)
        {
            covers[capability.CheckId] = [];
        }

        foreach (var obligation in proofPlan.Obligations.Where(item => item.Required && item.Kind != ObligationKind.Uncertainty))
        {
            var matched = false;
            foreach (var capability in catalog)
            {
                var coverage = _contracts.MatchCapability(obligation, capability, proofPlan.SourceDigest);
                if (coverage.Kind == CapabilityCoverageKind.Direct)
                {
                    covers[capability.CheckId].Add(obligation.Id);
                    exactCovered.Add(obligation.Id);
                    matched = true;
                }
            }

            if (!matched)
            {
                // 실행 전에 검사 범위를 다시 잡으면(OverrideTarget / TestFilter)
                // 의무는 여전히 정직하게 덮일 수 있다.
                var donor = FindOverrideDonor(catalog, obligation);
                if (donor is not null)
                {
                    covers[donor.CheckId].Add(obligation.Id);
                    overrideCovers.Add(new OverrideCover(
                        obligation.Id,
                        donor.CheckId,
                        obligation.Kind,
                        obligation.Subject?.Project ?? obligation.SubjectId,
                        obligation.Subject?.DisplayName ?? obligation.SubjectId));
                    matched = true;
                }
            }

            if (!matched)
            {
                uncovered.Add(new UncoveredObligation(
                    obligation.Id,
                    obligation.RuleId,
                    ProofReasonCodes.RequiredEvidenceMissing));
            }
        }

        var selectedIds = GreedySelect(catalog, covers, Weights(proofPlan));

        // producer-only 능력(API compatibility, test map, runtime
        // coverage)은 Distill 명령을 결코 실행하지 않는다. 생산자는 항상 돈다.
        // 그리디 set-cover는 P005에 가장 싼 생산자만 고르고
        // 나머지는 버린다. 그래서 무언가를 덮는 생산자는 모두 합친다.
        // 커버리지가 없는 생산자는 아무것도 기여하지 않고 선택되지 않은 채 남는다.
        UnionProducerCapabilities(catalog, covers, selectedIds);
        // 런타임 커버리지는 producer-only다. Distill은 실제 테스트 명령이
        // 돌 때만 Cobertura를 낸다. 커버리지가 선택되고 테스트 검사가
        // 없으면, 저장소 전체 테스트 호스트를 하나 고른다. 제품 심볼에서
        // 만든 클래스 필터는 결코 고르지 않는다(P010이 FullyQualifiedName~App.HomeController를
        // 지어내던 방식이다).
        SelectCoverageHostTests(catalog, selectedIds);

        var byId = catalog.ToDictionary(item => item.CheckId, StringComparer.OrdinalIgnoreCase);
        foreach (var id in selectedIds.ToArray())
        {
            foreach (var dependency in byId.GetValueOrDefault(id)?.DependsOn ?? [])
            {
                selectedIds.Add(dependency);
            }
        }

        var checks = MaterializeChecks(catalog, selectedIds, covers, exactCovered, overrideCovers, proofPlan);

        var definitionDigest = CertificateCanonicalHasher.HashText(string.Join('\n', checks.Select(item =>
            $"{item.CheckId}|{item.Kind}|{item.Command}|{string.Join(',', item.DependsOn ?? [])}")));

        return new VerificationPlan(checks, uncovered, profile, definitionDigest);
    }

    private sealed record OverrideCover(
        string ObligationId,
        string CheckId,
        ObligationKind Kind,
        string Project,
        string DisplayName);    private static IReadOnlyDictionary<string, int> Weights(ProofPlan proofPlan)
        => proofPlan.Obligations
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => Math.Max(group.First().RiskWeight, 1), StringComparer.Ordinal);

    private static HashSet<string> GreedySelect(
        IReadOnlyList<EvidenceCapability> catalog,
        IReadOnlyDictionary<string, HashSet<string>> covers,
        IReadOnlyDictionary<string, int> weights)
    {
        var remaining = covers.Values.SelectMany(set => set).ToHashSet(StringComparer.Ordinal);
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (remaining.Count == 0)
        {
            // 어떤 능력도 필수 의무를 덮지 않는다. 아무것도 실행하지 않는다.
            // 여기서 카탈로그 전체를 복사하면 아무도 필요 없는 검사를 실행한다.
            return selected;
        }

        while (remaining.Count > 0)
        {
            var best = catalog
                .Where(item => !selected.Contains(item.CheckId))
                .Select(item => (
                    item,
                    gain: covers[item.CheckId].Where(remaining.Contains).Sum(id => weights.GetValueOrDefault(id, 1)),
                    cost: Math.Max(item.Cost, 1)))
                .Where(item => item.gain > 0)
                .OrderByDescending(item => item.gain / (double)item.cost)
                .ThenBy(item => item.item.CheckId, StringComparer.Ordinal)
                .FirstOrDefault();

            if (best.item is null)
            {
                break;
            }

            selected.Add(best.item.CheckId);
            remaining.ExceptWith(covers[best.item.CheckId]);
        }

        return selected;
    }

    private static void UnionProducerCapabilities(
        IReadOnlyList<EvidenceCapability> catalog,
        IReadOnlyDictionary<string, HashSet<string>> covers,
        HashSet<string> selectedIds)
    {
        foreach (var capability in catalog)
        {
            if (!string.IsNullOrWhiteSpace(capability.CommandTarget))
            {
                continue;
            }

            if (!IsProducerCapabilityKind(capability.Kind))
            {
                continue;
            }

            if (covers.TryGetValue(capability.CheckId, out var covered) && covered.Count > 0)
            {
                selectedIds.Add(capability.CheckId);
            }
        }
    }

    private static void SelectCoverageHostTests(
        IReadOnlyList<EvidenceCapability> catalog,
        HashSet<string> selectedIds)
    {
        var coverageSelected = catalog.Any(item =>
            selectedIds.Contains(item.CheckId)
            && IsProducerCapabilityKind(item.Kind)
            && item.Kind.Contains("coverage", StringComparison.OrdinalIgnoreCase));
        if (!coverageSelected)
        {
            return;
        }

        if (catalog.Any(item =>
                selectedIds.Contains(item.CheckId)
                && string.Equals(item.Kind, "test", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var host = catalog
            .Where(item => string.Equals(item.Kind, "test", StringComparison.OrdinalIgnoreCase)
                           && !EvidenceCapabilityScope.IsScipTarget(item.CommandTarget))
            .Select(item => (item, exact: item.ScopeMode == ScopeMode.Exact, cost: Math.Max(item.Cost, 1)))
            .OrderBy(item => item.exact ? 1 : 0)
            .ThenBy(item => item.cost)
            .ThenBy(item => item.item.CheckId, StringComparer.Ordinal)
            .FirstOrDefault().item;
        if (host is not null)
        {
            selectedIds.Add(host.CheckId);
        }
    }

    private static bool IsProducerCapabilityKind(string kind)
        => EvidenceKindNames.IsProducerOnly(kind);

    private static EvidenceCapability? FindOverrideDonor(IReadOnlyList<EvidenceCapability> catalog, ProofObligation obligation)
    {
        var wantedKind = obligation.Kind is ObligationKind.Build or ObligationKind.CrossProject
            ? "build"
            : obligation.Kind is ObligationKind.Test or ObligationKind.CallerContract
                ? "test"
                : obligation.Kind == ObligationKind.StaticAnalysis
                    ? "analysis"
                    : null;
        if (wantedKind is null)
        {
            return null;
        }

        // donor를 순위 매긴다. 범위를 다시 잡은 클론은 검사를 다시 실행하므로,
        // 저장소 전체 능력이 (대상이 묶인) 정확한 능력보다 우선이다.
        // 그다음은 더 싼 검사, 그다음은 결정을 위한 CheckId 순서다.
        return catalog
            .Where(item => string.Equals(item.Kind, wantedKind, StringComparison.OrdinalIgnoreCase)
                           && !EvidenceCapabilityScope.IsScipTarget(item.CommandTarget))
            .Select(item => (item, exact: item.ScopeMode == ScopeMode.Exact, cost: Math.Max(item.Cost, 1)))
            .OrderBy(item => item.exact ? 1 : 0)
            .ThenBy(item => item.cost)
            .ThenBy(item => item.item.CheckId, StringComparer.Ordinal)
            .FirstOrDefault().item;
    }

    private static IReadOnlyList<PlannedVerificationCheck> MaterializeChecks(
        IReadOnlyList<EvidenceCapability> catalog,
        HashSet<string> selectedIds,
        IReadOnlyDictionary<string, HashSet<string>> covers,
        IReadOnlySet<string> exactCovered,
        IReadOnlyList<OverrideCover> overrideCovers,
        ProofPlan proofPlan)
    {
        var planned = new List<PlannedVerificationCheck>();
        foreach (var capability in catalog
                     .Where(item => selectedIds.Contains(item.CheckId))
                     .OrderBy(item => item.CheckId, StringComparer.Ordinal))
        {
            var covered = covers.GetValueOrDefault(capability.CheckId) ?? [];
            var exactIds = covered.Where(exactCovered.Contains).ToArray();
            var overrides = overrideCovers.Where(item => item.CheckId == capability.CheckId).ToArray();

            // base 검사는 자기 것의 정확한 커버리지를 가질 때(또는 평범한
            // 의존 대상으로 선택되었을 때)만 실행된다. Override
            // 의무는 대신 의무 범위 클론이 담당한다.
            if (exactIds.Length > 0 || overrides.Length == 0)
            {
                planned.Add(new PlannedVerificationCheck(
                    capability.CheckId,
                    capability.Kind,
                    capability.CommandTarget ?? string.Empty,
                    covered.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                    capability.DependsOn,
                    "covers-required-or-dependency"));
            }

            planned.AddRange(BuildCloneGroups(capability, overrides, proofPlan));
        }

        planned = planned.OrderBy(item => item.CheckId, StringComparer.Ordinal).ToList();
        var knownIds = planned.Select(item => item.CheckId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        planned = planned
            .Select(item => item.CheckId.Contains("::", StringComparison.Ordinal)
                ? item with { DependsOn = ResolveClonedDependencies(item, knownIds) }
                : item)
            .ToList();

        return AppendMissingDependencyBases(planned, catalog, selectedIds, covers);
    }

    private static IEnumerable<PlannedVerificationCheck> BuildCloneGroups(
        EvidenceCapability capability,
        IReadOnlyList<OverrideCover> overrides,
        ProofPlan proofPlan)
    {
        if (overrides.Count == 0)
        {
            yield break;
        }

        var obligationsById = proofPlan.Obligations.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var groups = new Dictionary<string, (string? OverrideTarget, string? TestFilter, List<string> ObligationIds)>(StringComparer.Ordinal);
        foreach (var item in overrides)
        {
            if (!obligationsById.TryGetValue(item.ObligationId, out var obligation))
            {
                continue;
            }

            string cloneId;
            string? overrideTarget = null;
            string? testFilter = null;
            if (obligation.Kind is ObligationKind.Build or ObligationKind.CrossProject or ObligationKind.StaticAnalysis)
            {
                overrideTarget = item.Project;
                cloneId = $"{capability.CheckId}::{SanitizeProject(item.Project)}";
            }
            else
            {
                // 클래스 단위 묶음. 같은 테스트 클래스로 도달한 의무는
                // 클론/필터 하나를 공유한다. 한 번의 실행이 테스트 심볼마다
                // 한 번씩이 아니라 그것들을 함께 덮는다.
                testFilter = TestClassFilter(item.DisplayName);
                cloneId = $"{capability.CheckId}::{CertificateCanonicalHasher.HashText(testFilter)[..8]}";
            }

            if (!groups.TryGetValue(cloneId, out var group))
            {
                groups[cloneId] = group = (overrideTarget, testFilter, []);
            }

            group.ObligationIds.Add(item.ObligationId);
        }

        foreach (var pair in groups.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            yield return new PlannedVerificationCheck(
                pair.Key,
                capability.Kind,
                capability.CommandTarget ?? string.Empty,
                pair.Value.ObligationIds.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                capability.DependsOn,
                "obligation-scoped-override",
                pair.Value.OverrideTarget,
                pair.Value.TestFilter);
        }
    }

    private static IReadOnlyList<string> ResolveClonedDependencies(
        PlannedVerificationCheck clone,
        IReadOnlySet<string> knownIds)
    {
        var separator = clone.CheckId.IndexOf("::", StringComparison.Ordinal);
        var suffix = clone.CheckId[(separator + 2)..];
        var resolved = new List<string>();
        foreach (var dependency in clone.DependsOn ?? [])
        {
            var clonedDependency = $"{dependency}::{suffix}";
            if (knownIds.Contains(clonedDependency))
            {
                resolved.Add(clonedDependency);
                continue;
            }

            var clones = knownIds
                .Where(id => id.StartsWith(dependency + "::", StringComparison.OrdinalIgnoreCase))
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            if (clones.Length > 0)
            {
                resolved.AddRange(clones);
                continue;
            }

            resolved.Add(dependency);
        }

        return resolved;
    }

    private static IReadOnlyList<PlannedVerificationCheck> AppendMissingDependencyBases(
        List<PlannedVerificationCheck> planned,
        IReadOnlyList<EvidenceCapability> catalog,
        HashSet<string> selectedIds,
        IReadOnlyDictionary<string, HashSet<string>> covers)
    {
        var knownIds = planned.Select(item => item.CheckId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var byId = catalog.ToDictionary(item => item.CheckId, StringComparer.OrdinalIgnoreCase);
        foreach (var missing in planned
                     .SelectMany(item => item.DependsOn ?? [])
                     .Where(id => !knownIds.Contains(id))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(id => id, StringComparer.Ordinal)
                     .ToArray())
        {
            if (!byId.TryGetValue(missing, out var capability) || !selectedIds.Contains(capability.CheckId))
            {
                continue;
            }

            var covered = covers.GetValueOrDefault(capability.CheckId) ?? [];
            planned.Add(new PlannedVerificationCheck(
                capability.CheckId,
                capability.Kind,
                capability.CommandTarget ?? string.Empty,
                covered.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                capability.DependsOn,
                "dependency-base"));
            knownIds.Add(capability.CheckId);
        }

        return planned.OrderBy(item => item.CheckId, StringComparer.Ordinal).ToList();
    }

    private static string SanitizeProject(string project)
        => project.Replace('\\', '_').Replace('/', '_');

    private static string TestClassFilter(string displayName)
    {
        var trimmed = SubjectIdentityMatcher.TrimSignature(displayName);
        var separator = trimmed.LastIndexOf('.');
        return separator > 0 ? trimmed[..separator] : trimmed;
    }

}
