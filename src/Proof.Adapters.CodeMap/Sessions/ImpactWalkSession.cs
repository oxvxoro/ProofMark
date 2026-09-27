using CodeMap.Core.Contracts;
using CodeMap.Core.Models;
using Proof.Core;

namespace Proof.Adapters.CodeMap.Sessions;

internal static class ImpactWalkSession
{
    internal static ResolvedImpactBudget ResolveBudget(
        ChangeRequest request,
        IReadOnlyList<ChangedSymbolRef>? changedSymbols = null,
        ImpactAnalysisSettings? settings = null)
    {
        settings ??= new ImpactAnalysisSettings();
        var hasPublic = changedSymbols?.Any(symbol => symbol.IsPublic) == true;
        var depth = hasPublic ? settings.PublicDepth : settings.BaseDepth;
        if (request.Spans.Count >= 20)
        {
            depth = Math.Max(depth, settings.PublicDepth);
        }

        return new ResolvedImpactBudget(
            depth,
            settings.MaxResults,
            Math.Max(1, settings.CallerPageSize),
            Math.Max(settings.CallerPageSize, settings.CallerMaxResults),
            settings.MinConfidence);
    }

    internal static (IReadOnlyList<RootedImpactItem> Items, bool Truncated) CollectRootedImpact(
        ICodeMapGraphReader reader,
        IReadOnlyList<IndexedSymbol> roots,
        ResolvedImpactBudget budget,
        string impactProfile)
    {
        var detailed = new List<RootedImpactItem>();
        var impactTruncated = false;
        foreach (var root in roots)
        {
            var offset = 0;
            while (true)
            {
                var page = reader.ImpactPaged(root, budget.Depth, budget.MaxResults, offset, impactProfile);
                foreach (var item in page.Items)
                {
                    detailed.Add(new RootedImpactItem(root.Id, item));
                }

                if (!page.HasMore)
                {
                    break;
                }

                impactTruncated = true;
                if (page.Items.Count == 0)
                {
                    break;
                }

                offset += page.Items.Count;
            }
        }

        return (detailed, impactTruncated);
    }

    /// <summary>
    /// 변경된 각 루트에 대해 루트 순서대로 head 그래프 호출자 관계를 덧붙이고
    /// (인증서 순서를 유지) 전역 호출자 예산이
    /// 완전한 순회를 막았는지 보고한다. 방문하지 않은 루트, 또는 예산이 소진된
    /// 뒤에도 결과가 더 있는 페이지는 완전함이 아니라
    /// 절단이다.
    /// </summary>
    internal static bool CollectCallers(
        ICodeMapGraphReader reader,
        IReadOnlyList<IndexedSymbol> roots,
        ResolvedImpactBudget budget,
        Func<IndexedSymbol, bool> isTestSymbol,
        List<string> callerSubjectIds,
        List<CallerRelation> callers)
    {
        var callerFetched = 0;
        var callerTruncated = false;

        for (var rootIndex = 0; rootIndex < roots.Count; rootIndex++)
        {
            var changed = roots[rootIndex];

            if (callerFetched >= budget.CallerMaxResults)
            {
                // 이 루트를 살펴보기 전에 예산이 소진되었다. 루트가
                // 방문되지 않은 채 남으므로, 순회는 불완전하다.
                callerTruncated = true;
                break;
            }

            var offset = 0;
            while (callerFetched < budget.CallerMaxResults)
            {
                var remaining = budget.CallerMaxResults - callerFetched;
                var limit = Math.Min(budget.CallerPageSize, remaining);
                var page = reader.CallerRelationsPaged(changed, limit, offset, budget.MinConfidence);
                foreach (var relation in page.Items)
                {
                    callerSubjectIds.Add(relation.Symbol.Id);
                    callers.Add(new CallerRelation(
                        changed.Id,
                        relation.Symbol.Id,
                        relation.Symbol.Project,
                        relation.Symbol.RelativePath,
                        relation.Edge.Kind.ToString(),
                        relation.Edge.Confidence,
                        isTestSymbol(relation.Symbol)));
                    callerFetched++;
                }

                if (!page.HasMore)
                {
                    break;
                }

                if (page.Items.Count == 0)
                {
                    // 리더는 결과가 더 있다고 했지만 진전이 없다.
                    callerTruncated = true;
                    break;
                }

                offset += page.Items.Count;
                if (callerFetched >= budget.CallerMaxResults)
                {
                    callerTruncated = true;
                    break;
                }
            }

            if (callerFetched >= budget.CallerMaxResults && rootIndex < roots.Count - 1)
            {
                // 예산이 소진되었고 이후 루트는 살펴보지 않았다.
                callerTruncated = true;
                break;
            }
        }

        return callerTruncated;
    }

    internal static async Task<ChangeImpact> BuildAsync(
        ChangeRequest request,
        IReadOnlyList<IndexedSymbol> roots,
        IReadOnlyList<ChangedSymbolRef> changedSymbols,
        DeletionImpactResult? deletionImpact,
        ICodeMapGraphReader reader,
        ResolvedImpactBudget budget,
        string impactProfile,
        int unknownSpanCount,
        TestProjectClassifier testClassifier,
        bool runArchitectureCheck,
        string workspaceRoot,
        string databasePath,
        CancellationToken cancellationToken)
    {
        var (detailed, impactTruncated) = CollectRootedImpact(reader, roots, budget, impactProfile);

        var impactItems = detailed.Select(item => item.Item).ToList();
        var relations = detailed
            .Select(item => new ImpactRelation(
                item.RootId,
                item.Item.Symbol.Id,
                item.Item.Depth,
                item.Item.Via.Kind.ToString(),
                item.Item.Via.ResolutionKind.ToString(),
                item.Item.Via.Confidence))
            .ToArray();

        var impactedSymbols = impactItems
            .DistinctBy(item => item.Symbol.Id, StringComparer.Ordinal)
            .Select(item => ToImpactedSymbolRef(item, testClassifier))
            .ToArray();

        if (deletionImpact is not null)
        {
            impactedSymbols = impactedSymbols
                .Concat(deletionImpact.ImpactedSymbols)
                .DistinctBy(item => item.Id, StringComparer.Ordinal)
                .ToArray();
        }

        var callerSubjectIds = new List<string>();
        var callers = new List<CallerRelation>();
        var mergedChangedSymbols = changedSymbols.ToList();
        if (deletionImpact is not null)
        {
            // 완전함을 평가하기 전에 삭제 절단을 병합한다. 예산이 제한한
            // base 그래프 순회를 완전하다고 보고해서는 안 된다.
            impactTruncated |= deletionImpact.ImpactPotentiallyTruncated;

            callers.AddRange(deletionImpact.CallerRelations);
            foreach (var caller in deletionImpact.CallerRelations)
            {
                callerSubjectIds.Add(caller.CallerSymbolId);
            }

            mergedChangedSymbols = mergedChangedSymbols
                .Concat(deletionImpact.DeletedSymbols)
                .DistinctBy(symbol => symbol.Id, StringComparer.Ordinal)
                .ToList();
        }

        var callerTruncated = CollectCallers(
            reader,
            roots,
            budget,
            testClassifier.IsTestSymbol,
            callerSubjectIds,
            callers);
        if (deletionImpact is not null)
        {
            callerTruncated |= deletionImpact.CallerPotentiallyTruncated;
        }

        var impactedProjects = mergedChangedSymbols
            .Select(symbol => symbol.Project)
            .Concat(impactedSymbols.Select(symbol => symbol.Project))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var heuristicCount = impactItems.Count(item =>
            item.Via.ResolutionKind == EdgeResolutionKind.Heuristic
            || (item.Via.Confidence ?? 1) < budget.MinConfidence);
        var evaluation = CodeMapCompletenessEvaluator.Evaluate(
            unknownSpanCount,
            impactItems.Count,
            impactTruncated,
            callerTruncated,
            heuristicCount,
            impactItems.Count == 0 ? null : impactItems.Min(item => item.Via.Confidence ?? 1),
            budget);

        IReadOnlyList<ArchitectureViolationRef>? architectureViolations = null;
        bool? architectureRulesPresent = null;
        string? architectureRulesDigest = null;
        if (runArchitectureCheck)
        {
            var (violations, rulesPresent, rulesDigest) = await ArchitectureCheckRunner
                .RunAsync(workspaceRoot, databasePath, cancellationToken).ConfigureAwait(false);
            architectureViolations = violations;
            architectureRulesPresent = rulesPresent;
            architectureRulesDigest = rulesDigest;
        }

        return new ChangeImpact(
            request.BaseRevision,
            request.HeadRevision,
            request.Spans,
            mergedChangedSymbols,
            impactedSymbols,
            impactedProjects,
            callerSubjectIds.Distinct(StringComparer.Ordinal).ToArray(),
            evaluation.CoverageStatus,
            evaluation.HasHeuristic,
            relations,
            callers,
            evaluation.Completeness,
            evaluation.Constraints,
            request.SourceDigest,
            request.UsedFileWideFallback,
            DeletionPathsResolved: deletionImpact?.ResolvedPaths,
            ArchitectureViolations: architectureViolations,
            ArchitectureRulesPresent: architectureRulesPresent,
            ArchitectureRulesDigest: architectureRulesDigest);
    }

    internal static ChangedSymbolRef ToChangedSymbolRef(IndexedSymbol symbol, TestProjectClassifier testClassifier) =>
        new(
            symbol.Id,
            symbol.Project,
            symbol.RelativePath,
            SymbolDisplayName.For(symbol),
            symbol.StartLine,
            symbol.EndLine,
            string.Equals(symbol.Visibility, "public", StringComparison.OrdinalIgnoreCase),
            testClassifier.IsTestSymbol(symbol));

    private static ImpactedSymbolRef ToImpactedSymbolRef(ImpactItem item, TestProjectClassifier testClassifier) =>
        new(
            item.Symbol.Id,
            item.Symbol.Project,
            item.Symbol.RelativePath,
            SymbolDisplayName.For(item.Symbol),
            item.RootId ?? string.Empty,
            item.Depth,
            testClassifier.IsTestSymbol(item.Symbol),
            item.Via.ResolutionKind.ToString(),
            item.Via.Confidence);
}
