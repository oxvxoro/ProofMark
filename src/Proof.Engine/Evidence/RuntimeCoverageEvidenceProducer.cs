using Proof.Core;

namespace Proof.Engine;

/// <summary>
/// 실행된 메서드 커버리지(Distill 테스트 실행이 낸 Cobertura XML)를
/// P005 의무의 RuntimeCoverage 증거로 바꾼다. 커버리지 적중은 그것이
/// 가리키는 주체만 증명한다. 일치는 정확한 테스트 맵 신원
/// 함수를 재사용하고, 어떤 의무와도 맞지 않는 적중은 버린다. 적중 하나가
/// 그것이 맞는 의무보다 더 많이 결코 닫지 않는다. 커버리지 산출물이 없으면
/// 증거도 없다(지어낸 Pass는 결코 아니다).
/// </summary>
public sealed class RuntimeCoverageEvidenceProducer : IRuntimeCoverageEvidenceProducer
{
    public Task<IReadOnlyList<ProofEvidence>> AnalyzeAsync(
        ChangeRequest request,
        ChangeImpact impact,
        ProofPlan proofPlan,
        IReadOnlyList<string> coverageArtifactPaths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(impact);
        ArgumentNullException.ThrowIfNull(proofPlan);
        cancellationToken.ThrowIfCancellationRequested();

        if (coverageArtifactPaths is not { Count: > 0 } || proofPlan.Obligations.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<ProofEvidence>>([]);
        }

        var hits = ReadExecutedMethods(request.WorkspaceRoot, coverageArtifactPaths);
        if (hits.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<ProofEvidence>>([]);
        }

        var evidence = new List<ProofEvidence>();
        var index = 0;
        foreach (var obligation in proofPlan.Obligations.Where(item =>
            item.Kind is ObligationKind.TestMapping or ObligationKind.AppContract))
        {
            var matched = hits.FirstOrDefault(hit =>
                TestMapMatching.SymbolMatches(obligation.SubjectId, obligation.Subject?.DisplayName, hit.Symbol));
            if (matched is null && obligation.Kind == ObligationKind.AppContract)
            {
                matched = MatchRouteHandler(impact, obligation.SubjectId, hits);
            }

            if (matched is null)
            {
                continue;
            }

            // 구체적인 커버리지 산출물(과 그 다이제스트)을 붙인다. 인증서
            // 매니페스트가 이 의무를 증명하는 파일을 고정하게 한다.
            var fullPath = Path.IsPathRooted(matched.RelativePath)
                ? matched.RelativePath
                : Path.Combine(request.WorkspaceRoot, matched.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            index++;
            evidence.Add(new ProofEvidence(
                $"RC{index}",
                EvidenceKind.RuntimeCoverage,
                obligation.SubjectId,
                EvidenceStatus.Pass,
                new EvidenceProvenance(
                    "runtime-coverage",
                    ArtifactPointer: matched.RelativePath,
                    SourceDigest: request.SourceDigest ?? proofPlan.SourceDigest,
                    CheckId: EvidenceCheckIds.RuntimeCoverage,
                    Sha256: ArtifactHasher.ComputeSha256Hex(fullPath)),
                new EvidenceScope(
                    ScopeMode.Exact,
                    CommandTarget: null,
                    SubjectRefs:
                    [
                        new EvidenceSubjectRef(
                            SubjectKind.Symbol,
                            obligation.SubjectId,
                            obligation.Subject?.Project,
                            obligation.Subject?.File,
                            obligation.Subject?.DisplayName)
                    ])));
        }

        return Task.FromResult<IReadOnlyList<ProofEvidence>>(evidence);
    }

    // 라우트 노드는 Cobertura 메서드가 아니다. 라우트가 깊이 1의 semantic RoutesTo로
    // 바뀐 처리기에 곧바로 이어질 때만 그 처리기의 실행이 라우트 계약을 증명한다.
    // 더 깊은 관계는 중간 처리기를 알 수 없으므로 닫지 않는다.
    private static ExecutedMethod? MatchRouteHandler(
        ChangeImpact impact,
        string routeSubjectId,
        IReadOnlyList<ExecutedMethod> hits)
    {
        foreach (var relation in impact.Relations ?? [])
        {
            if (relation is not { Depth: 1, EdgeKind: "RoutesTo" }
                || !string.Equals(relation.ResolutionKind, "Semantic", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(relation.ImpactedSymbolId, routeSubjectId, StringComparison.Ordinal))
            {
                continue;
            }

            var handler = impact.ChangedSymbols.FirstOrDefault(item =>
                string.Equals(item.Id, relation.RootChangedSymbolId, StringComparison.Ordinal));
            if (handler is null || handler.IsTest)
            {
                continue;
            }

            var matched = hits.FirstOrDefault(hit =>
                TestMapMatching.SymbolMatches(handler.Id, handler.DisplayName, hit.Symbol));
            if (matched is not null)
            {
                return matched;
            }
        }

        return null;
    }

    private static IReadOnlyList<ExecutedMethod> ReadExecutedMethods(
        string workspaceRoot,
        IReadOnlyList<string> coverageArtifactPaths)
    {
        var methods = new List<ExecutedMethod>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relativePath in coverageArtifactPaths.OrderBy(path => path, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                continue;
            }

            var fullPath = Path.IsPathRooted(relativePath)
                ? relativePath
                : Path.Combine(workspaceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            try
            {
                foreach (var symbol in CoberturaCoverageParser.Parse(File.ReadAllText(fullPath)))
                {
                    if (seen.Add(symbol + "@" + relativePath))
                    {
                        methods.Add(new ExecutedMethod(symbol, relativePath));
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 읽을 수 없는 커버리지 파일은 아무것도 기여하지 않는다.
                // 긍정 신호로 바뀌어서는 안 된다.
            }
        }

        return methods
            .OrderBy(method => method.Symbol, StringComparer.Ordinal)
            .ThenBy(method => method.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    internal sealed record ExecutedMethod(string Symbol, string RelativePath);
}
