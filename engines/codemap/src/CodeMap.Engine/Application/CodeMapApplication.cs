using CodeMap.CSharp;
using CodeMap.Storage;
using CodeMap.Core.Contracts;
using CodeMap.Core.Models;
using CodeMap.Engine.Concurrency;
using CodeMap.Engine.Application.Investigation;
using CodeMap.Core.Models.Investigation;
using CodeMap.Engine.Indexing;

namespace CodeMap.Engine.Application;

/// <summary>
/// 전송 방식과 무관한 CodeMap 유스케이스. JSON, 종료 코드, 프로토콜
/// 봉투는 의도적으로 CLI/MCP/LSP 어댑터에 남긴다.
/// </summary>
public sealed class CodeMapApplication
{
    private readonly IIndexFreshnessService? _freshness;
    private readonly Func<IncrementalCodeMapIndexer> _indexerFactory;
    private readonly Func<string, CancellationToken, Task<ICodeMapGraphReader>> _graphReaderFactory;
    private readonly KeyedAsyncLock<string> _sliceLocks = new();

    public CodeMapApplication(
        IIndexFreshnessService? freshness = null,
        Func<IncrementalCodeMapIndexer>? indexerFactory = null,
        Func<string, CancellationToken, Task<ICodeMapGraphReader>>? graphReaderFactory = null)
    {
        _freshness = freshness;
        _indexerFactory = indexerFactory ?? IncrementalCodeMapIndexerFactory.Create;
        _graphReaderFactory = graphReaderFactory
            ?? throw new ArgumentNullException(nameof(graphReaderFactory),
                "A graph reader factory must be supplied by the storage adapter.");
    }

    public async Task<ApplicationResponse<ApplicationFindResult>> FindAsync(
        FindRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Query))
            return ApplicationResponse<ApplicationFindResult>.Failure(new("query_failed", "A symbol query is required."));

        return await WithServiceAsync(request.Root, cancellationToken, (service, stale) =>
        {
            var resolution = service.ResolveSymbol(request.Query, callableOnly: false, request.MaxResults);
            var members = resolution.Matches.ToDictionary(
                symbol => symbol.Id,
                symbol => service.Members(symbol),
                StringComparer.Ordinal);
            return ApplicationResponse<ApplicationFindResult>.Success(new ApplicationFindResult(resolution, members), stale);
        });
    }

    public async Task<ApplicationResponse<ApplicationContextResult>> ContextAsync(
        ContextRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Task))
            return ApplicationResponse<ApplicationContextResult>.Failure(
                new("query_failed", "A context task is required."));

        return await WithServiceAsync(request.Root, cancellationToken, (service, stale) =>
        {
            var matches = service.Find(request.Task, request.MaxResults);
            var map = service.BuildMap(matches.FirstOrDefault()?.Name ?? request.Task, null, request.TokenBudget);
            return ApplicationResponse<ApplicationContextResult>.Success(
                new ApplicationContextResult(matches, map), stale);
        });
    }

    public async Task<ApplicationResponse<IReadOnlyList<RelationQueryResult>>> RelationsAsync(
        RelationsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!RelationConfidence.IsValid(request.MinConfidence))
            return ApplicationResponse<IReadOnlyList<RelationQueryResult>>.Failure(
                new("query_failed", RelationConfidence.InvalidMessage));

        return await WithServiceAsync(request.Root, cancellationToken, (service, stale) =>
        {
            var source = ResolveUnique(service, request.Source, request.MaxResults, out var sourceError);
            if (sourceError is not null)
                return ApplicationResponse<IReadOnlyList<RelationQueryResult>>.Failure(sourceError, stale);
            var target = ResolveUnique(service, request.Target, request.MaxResults, out var targetError);
            if (targetError is not null)
                return ApplicationResponse<IReadOnlyList<RelationQueryResult>>.Failure(targetError, stale);

            var edgeKind = request.EdgeKind is null || !Enum.TryParse<EdgeKind>(request.EdgeKind, true, out var parsed)
                ? (EdgeKind?)null
                : parsed;
            return ApplicationResponse<IReadOnlyList<RelationQueryResult>>.Success(
                service.Relations(source!.Id, target!.Id, edgeKind, request.MaxResults, request.MinConfidence), stale);
        });
    }

    public async Task<ApplicationResponse<ApplicationImpactResult>> ImpactAsync(
        ImpactRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!CodeMapQueryValidation.IsValidImpactProfile(request.Profile))
            return ApplicationResponse<ApplicationImpactResult>.Failure(
                new("query_failed", "profile must be one of: code, app."));
        if (!RelationConfidence.IsValid(request.MinConfidence))
            return ApplicationResponse<ApplicationImpactResult>.Failure(
                new("query_failed", RelationConfidence.InvalidMessage));

        return await WithServiceAsync(request.Root, cancellationToken, (service, stale) =>
        {
            var symbol = ResolveUnique(service, request.Query, request.MaxResults, out var error);
            if (error is not null)
                return ApplicationResponse<ApplicationImpactResult>.Failure(error, stale);
            var items = service.Impact(symbol!, request.Depth, request.MaxResults, request.Profile)
                .Where(item => (item.Via.Confidence ?? 1) >= request.MinConfidence)
                .ToArray();
            var files = service.FindFilesByIds(items.Select(item => item.Via.SourceFileId).OfType<string>());
            return ApplicationResponse<ApplicationImpactResult>.Success(
                new ApplicationImpactResult(symbol!, items, files), stale);
        });
    }

    public async Task<ApplicationResponse<ApplicationFlowResult>> FlowAsync(
        FlowRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!CodeMapQueryValidation.IsValidFlowKind(request.Kind))
            return ApplicationResponse<ApplicationFlowResult>.Failure(
                new("query_failed", "kind must be one of: http, ui, all."));
        if (!CodeMapQueryValidation.IsValidFlowDepth(request.Depth))
            return ApplicationResponse<ApplicationFlowResult>.Failure(
                new("query_failed", $"depth must be between 1 and 8."));
        if (!RelationConfidence.IsValid(request.MinConfidence))
            return ApplicationResponse<ApplicationFlowResult>.Failure(
                new("query_failed", RelationConfidence.InvalidMessage));

        return await WithServiceAsync(request.Root, cancellationToken, (service, stale) =>
        {
            var entry = ResolveUnique(service, request.Entry, request.MaxResults, out var error);
            if (error is not null)
                return ApplicationResponse<ApplicationFlowResult>.Failure(error, stale);
            var items = service.Flow(entry!, request.Kind, request.Depth, request.MaxResults, request.MinConfidence);
            var sources = service.FindByIds(items.Select(item => item.Via.SourceId));
            var files = service.FindFilesByIds(items.Select(item => item.Via.SourceFileId).OfType<string>());
            return ApplicationResponse<ApplicationFlowResult>.Success(
                new ApplicationFlowResult(entry!, items, sources, files), stale);
        });
    }

    public Task<ApplicationResponse<ApplicationRelationListResult>> CallersAsync(
        SymbolRelationRequest request,
        CancellationToken cancellationToken = default)
        => RelationListAsync(request, callableOnly: true, RelationKind.Callers, cancellationToken);

    public Task<ApplicationResponse<ApplicationRelationListResult>> CalleesAsync(
        SymbolRelationRequest request,
        CancellationToken cancellationToken = default)
        => RelationListAsync(request, callableOnly: true, RelationKind.Callees, cancellationToken);

    public Task<ApplicationResponse<ApplicationRelationListResult>> RefsAsync(
        SymbolRelationRequest request,
        CancellationToken cancellationToken = default)
        => RelationListAsync(request, callableOnly: false, RelationKind.Refs, cancellationToken);

    public Task<ApplicationResponse<ApplicationRelationListResult>> ImplAsync(
        SymbolRelationRequest request,
        CancellationToken cancellationToken = default)
        => RelationListAsync(request, callableOnly: false, RelationKind.Impl, cancellationToken);

    public async Task<ApplicationResponse<ApplicationChangedImpactResult>> ChangedImpactAsync(
        ChangedImpactRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!CodeMapQueryValidation.IsValidImpactProfile(request.Profile))
            return ApplicationResponse<ApplicationChangedImpactResult>.Failure(
                new("query_failed", "profile must be one of: code, app."));
        if (!RelationConfidence.IsValid(request.MinConfidence))
            return ApplicationResponse<ApplicationChangedImpactResult>.Failure(
                new("query_failed", RelationConfidence.InvalidMessage));

        try
        {
            var queryRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(request.Root)
                ? Directory.GetCurrentDirectory()
                : request.Root);
            var changedFilesResult = GitChangedFileResolver.ListChangedFilesWithStatus(
                queryRoot,
                request.BaseRevision,
                cancellationToken);
            return await WithServiceAsync(request.Root, cancellationToken, (service, stale) =>
            {
                var directSymbols = service.SymbolsInFiles(changedFilesResult.NewPathChangedFiles, int.MaxValue);
                var analysisImpact = service.ImpactUnion(directSymbols, request.Depth, int.MaxValue, request.Profile)
                    .Where(item => (item.Via.Confidence ?? 1) >= request.MinConfidence)
                    .Take(Math.Max(1, request.MaxResults))
                    .ToArray();
                return ApplicationResponse<ApplicationChangedImpactResult>.Success(
                    new ApplicationChangedImpactResult(
                        changedFilesResult.AllChangedPaths,
                        directSymbols.Take(request.MaxResults).ToArray(),
                        analysisImpact,
                        changedFilesResult.AnalysisComplete,
                        changedFilesResult.UnresolvableChangedPaths),
                    stale);
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ApplicationResponse<ApplicationChangedImpactResult>.Failure(Classify(exception), IsStaleFailure(exception));
        }
    }

    private enum RelationKind
    {
        Callers,
        Callees,
        Refs,
        Impl
    }

    private async Task<ApplicationResponse<ApplicationRelationListResult>> RelationListAsync(
        SymbolRelationRequest request,
        bool callableOnly,
        RelationKind kind,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!RelationConfidence.IsValid(request.MinConfidence))
            return ApplicationResponse<ApplicationRelationListResult>.Failure(
                new("query_failed", RelationConfidence.InvalidMessage));

        return await WithServiceAsync(request.Root, cancellationToken, (service, stale) =>
        {
            var resolution = service.ResolveSymbol(request.Query, callableOnly, request.MaxResults);
            if (resolution.Matches.Count == 0)
                return ApplicationResponse<ApplicationRelationListResult>.Failure(
                    new("no_matches", $"No symbol matches '{request.Query}'."), stale);
            if (resolution.IsAmbiguous || resolution.Matches.Count > 1)
                return ApplicationResponse<ApplicationRelationListResult>.Failure(
                    new("ambiguous", $"Symbol query '{request.Query}' is ambiguous."), stale);

            var symbol = resolution.Matches[0];
            IReadOnlyList<IndexedRelation> relations = kind switch
            {
                RelationKind.Callers => service.CallerRelations(symbol, request.MaxResults)
                    .Concat(service.ReferencedByRelations(symbol, request.MaxResults))
                    .Where(item => (item.Edge.Confidence ?? 1) >= request.MinConfidence)
                    .DistinctBy(item => item.Symbol.Id, StringComparer.Ordinal)
                    .Take(request.MaxResults)
                    .ToArray(),
                RelationKind.Callees => service.CalleeRelations(symbol, request.Depth, request.MaxResults)
                    .Where(item => (item.Edge.Confidence ?? 1) >= request.MinConfidence).ToArray(),
                RelationKind.Impl => service.ImplementationRelations(symbol, request.MaxResults)
                    .Where(item => (item.Edge.Confidence ?? 1) >= request.MinConfidence).ToArray(),
                RelationKind.Refs => service.ImplementationRelations(symbol, request.MaxResults)
                    .Concat(service.ReferencedByRelations(symbol, request.MaxResults))
                    .Where(item => (item.Edge.Confidence ?? 1) >= request.MinConfidence)
                    .Take(request.MaxResults)
                    .ToArray(),
                _ => Array.Empty<IndexedRelation>()
            };
            return ApplicationResponse<ApplicationRelationListResult>.Success(
                new ApplicationRelationListResult(symbol, relations), stale);
        });
    }

    public async Task<ApplicationResponse<InvestigationResult>> InvestigateAsync(
        InvestigationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Query))
            return ApplicationResponse<InvestigationResult>.Failure(
                new("query_failed", "A symbol query is required."));
        if (request.TokenBudget <= 0 || request.MaxResults <= 0)
            return ApplicationResponse<InvestigationResult>.Failure(
                new("query_failed", "tokens and max-results must be greater than zero."));
        if (!RelationConfidence.IsValid(request.MinConfidence))
            return ApplicationResponse<InvestigationResult>.Failure(
                new("query_failed", RelationConfidence.InvalidMessage));
        if (request.Depth is <= 0 or > 8)
            return ApplicationResponse<InvestigationResult>.Failure(
                new("query_failed", "depth must be between 1 and 8."));
        if (request.SourceMode is not ("none" or "minimal" or "scope"))
            return ApplicationResponse<InvestigationResult>.Failure(
                new("query_failed", "source-mode must be one of: none, minimal, scope."));

        return await WithInvestigationServiceAsync(request.Root, cancellationToken, async (service, stale, indexRoot) =>
        {
            var resolution = service.ResolveSymbol(request.Query, callableOnly: false, request.MaxResults);
            if (resolution.Matches.Count == 0)
                return ApplicationResponse<InvestigationResult>.Failure(
                    new("no_matches", $"No symbol matches '{request.Query}'."), stale);
            if (resolution.IsAmbiguous || resolution.Matches.Count > 1)
                return ApplicationResponse<InvestigationResult>.Failure(
                    new("ambiguous", $"Symbol query '{request.Query}' is ambiguous."),
                    stale,
                    new InvestigationResult(
                        new InvestigationResponse(1, request.Query, request.Goal, null, true, resolution.Matches),
                        Array.Empty<InvestigationCandidate>(),
                        new InvestigationBudgetAllocator().Allocate(Array.Empty<InvestigationCandidate>(), request.TokenBudget),
                        InvestigationCoverage.Empty,
                        Array.Empty<InvestigationSourceSpan>()));

            var root = resolution.Matches[0];
            var overrides = new InvestigationOverrides(
                request.MaxResults,
                request.Depth ?? (request.Goal == InvestigationGoal.Trace ? 4 : request.Goal == InvestigationGoal.Impact ? 2 : 1),
                request.MinConfidence,
                request.IncludeHeuristic,
                request.TokenBudget,
                indexRoot);
            var orchestrator = new InvestigationOrchestrator(
                new InvestigationRankingPolicy(),
                new InvestigationBudgetAllocator(),
                goal => InvestigationProfiles.Create(goal, SliceForInvestigationAsync));
            var result = await orchestrator.RunAsync(request.Goal, service, root, overrides, cancellationToken);
            var files = service.FindFilesByIds(result.Selection.Selected
                .Concat(result.Selection.Excluded)
                .Select(candidate => candidate.Via?.SourceFileId)
                .OfType<string>());
            var locatedCandidates = result.Candidates.Select(candidate => AttachEvidenceLocation(candidate, files)).ToArray();
            var locatedSelection = result.Selection with
            {
                Selected = result.Selection.Selected.Select(candidate => AttachEvidenceLocation(candidate, files)).ToArray(),
                Excluded = result.Selection.Excluded.Select(candidate => AttachEvidenceLocation(candidate, files)).ToArray()
            };
            var locatedResult = result with { Candidates = locatedCandidates, Selection = locatedSelection };
            var coverage = new CoverageAggregator().Aggregate(
                locatedResult.ProviderStatuses,
                locatedResult.Selection.Selected.Concat(locatedResult.Selection.Excluded).ToArray(),
                locatedResult.Selection.Selected,
                service,
                root,
                request);
            var sourceMode = Enum.Parse<SourceEvidenceMode>(request.SourceMode, ignoreCase: true);
            var sourceSpans = new SourceEvidenceBuilder().BuildSpans(
                locatedSelection.Selected,
                sourceMode,
                Math.Max(0, request.TokenBudget - locatedResult.Selection.EstimatedTokens),
                new FileTextAccessor(indexRoot));
            var sourceTokens = sourceSpans.Sum(span => Math.Max(1, (int)Math.Ceiling(span.Text.Length / 4d)));
            var finalSelection = locatedSelection with
            {
                Cost = locatedSelection.Cost with
                {
                    Source = sourceTokens,
                    TotalEstimated = locatedSelection.Cost.Structural + sourceTokens
                }
            };
            var response = new InvestigationResponse(1, request.Query, request.Goal, root, false, Array.Empty<IndexedSymbol>());
            return ApplicationResponse<InvestigationResult>.Success(
                new InvestigationResult(response, locatedResult.Candidates, finalSelection, coverage, sourceSpans), stale);
        });

        async Task<SemanticSliceResult> SliceForInvestigationAsync(
            IndexedSymbol symbol, string projectRoot, CancellationToken token)
        {
            return await new CodeMapSemanticSliceService(_indexerFactory()).SliceAsync(
                projectRoot,
                new SemanticSliceRequest(Query: symbol.QualifiedName, MaxResults: request.MaxResults),
                token);
        }
    }

    public async Task<ApplicationResponse<CodeMapIndexStatus>> StatusAsync(
        StatusRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var database = CodeMapIndexLocator.FindDatabase(Path.GetFullPath(request.Root));
            var status = await CodeMapIndexStatusReader.ReadAsync(database, cancellationToken);
            var stale = request.CheckFreshness && _freshness is not null
                ? await CodeMapIndexLocator.IsStaleAsync(database, cancellationToken, _freshness.IsUpToDateAsync)
                : false;
            return ApplicationResponse<CodeMapIndexStatus>.Success(status, stale);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ApplicationResponse<CodeMapIndexStatus>.Failure(Classify(exception));
        }
    }

    public async Task<ApplicationResponse<SemanticSliceResult>> SemanticSliceAsync(
        string root,
        SemanticSliceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(request);
        var key = Path.GetFullPath(root);
        await using var lease = await _sliceLocks.AcquireAsync(key, cancellationToken);
        try
        {
            var result = await new CodeMapSemanticSliceService(_indexerFactory()).SliceAsync(key, request, cancellationToken);
            return ApplicationResponse<SemanticSliceResult>.Success(result);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ApplicationResponse<SemanticSliceResult>.Failure(Classify(exception));
        }
    }

    public async Task<ApplicationResponse<LocateChangedSymbolsResult>> LocateChangedSymbolsAsync(
        LocateChangedSymbolsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Spans.Count == 0)
        {
            return ApplicationResponse<LocateChangedSymbolsResult>.Success(
                new LocateChangedSymbolsResult([], []));
        }

        return await WithServiceAsync(request.Root, cancellationToken, (service, stale) =>
        {
            var symbols = new Dictionary<string, IndexedSymbol>(StringComparer.Ordinal);
            var locationUnknown = new List<ChangedFileSpan>();

            foreach (var span in request.Spans)
            {
                var matches = service.SymbolsIntersecting(
                    span.RelativePath,
                    span.StartLine,
                    span.EndLine,
                    request.MaxResultsPerSpan);

                if (matches.Count == 0)
                {
                    locationUnknown.Add(span);
                    continue;
                }

                foreach (var symbol in matches)
                {
                    symbols[symbol.Id] = symbol;
                }
            }

            var ordered = symbols.Values
                .OrderBy(symbol => symbol.QualifiedName, StringComparer.Ordinal)
                .ThenBy(symbol => symbol.Id, StringComparer.Ordinal)
                .ToArray();

            return ApplicationResponse<LocateChangedSymbolsResult>.Success(
                new LocateChangedSymbolsResult(ordered, locationUnknown), stale);
        });
    }

    public async Task<ApplicationResponse<IndexSummary>> RefreshIndexAsync(
        RefreshIndexRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var indexer = _indexerFactory();
            var summary = request.Force
                ? await indexer.IndexAsync(request.Root, force: true, cancellationToken)
                : await indexer.UpdateAsync(request.Root, cancellationToken);
            _freshness?.Invalidate(Path.GetFullPath(request.Root));
            return ApplicationResponse<IndexSummary>.Success(summary);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ApplicationResponse<IndexSummary>.Failure(Classify(exception));
        }
    }

    private async Task<ApplicationResponse<T>> WithServiceAsync<T>(
        string? requestedRoot,
        CancellationToken cancellationToken,
        Func<ICodeMapGraphReader, bool, ApplicationResponse<T>> action)
    {
        try
        {
            var queryRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(requestedRoot)
                ? Directory.GetCurrentDirectory()
                : requestedRoot);
            var database = CodeMapIndexLocator.FindDatabase(queryRoot);
            var readinessFailure = await TryGuardIndexReadinessAsync<T>(database, cancellationToken);
            if (readinessFailure is not null)
            {
                return readinessFailure;
            }

            var stale = _freshness is not null
                && await CodeMapIndexLocator.IsStaleAsync(
                    database,
                    cancellationToken,
                    _freshness.IsUpToDateAsync);
            await using var reader = await _graphReaderFactory(database, cancellationToken);
            return action(reader, stale);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ApplicationResponse<T>.Failure(Classify(exception), IsStaleFailure(exception));
        }
    }

    private async Task<ApplicationResponse<T>> WithInvestigationServiceAsync<T>(
        string? requestedRoot,
        CancellationToken cancellationToken,
        Func<ICodeMapGraphReader, bool, string, Task<ApplicationResponse<T>>> action)
    {
        try
        {
            var queryRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(requestedRoot)
                ? Directory.GetCurrentDirectory()
                : requestedRoot);
            var database = CodeMapIndexLocator.FindDatabase(queryRoot);
            var readinessFailure = await TryGuardIndexReadinessAsync<T>(database, cancellationToken);
            if (readinessFailure is not null)
            {
                return readinessFailure;
            }

            var indexRoot = CodeMapIndexLocator.ResolveIndexRoot(database);
            var stale = _freshness is not null
                && await CodeMapIndexLocator.IsStaleAsync(
                    database,
                    cancellationToken,
                    _freshness.IsUpToDateAsync);
            await using var reader = await _graphReaderFactory(database, cancellationToken);
            return await action(reader, stale, indexRoot);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ApplicationResponse<T>.Failure(Classify(exception), IsStaleFailure(exception));
        }
    }

    private static InvestigationCandidate AttachEvidenceLocation(
        InvestigationCandidate candidate,
        IReadOnlyDictionary<string, IndexedFile> files)
    {
        if (candidate.LocalEvidence is not null || candidate.Via?.SourceFileId is not { } fileId)
        {
            return candidate.EvidenceLocation is not null
                ? candidate
                : candidate with
                {
                    EvidenceLocation = new InvestigationEvidenceLocation(
                        candidate.Symbol.RelativePath,
                        candidate.Symbol.StartLine,
                        null,
                        candidate.Symbol.EndLine,
                        null,
                        InvestigationEvidenceLocationOrigin.SymbolDeclaration)
                };
        }

        if (!files.TryGetValue(fileId, out var file))
            return candidate;

        return candidate with
        {
            EvidenceLocation = new InvestigationEvidenceLocation(
                file.RelativePath,
                candidate.Via.Line,
                candidate.Via.StartColumn,
                candidate.Via.EndLine,
                candidate.Via.EndColumn,
                InvestigationEvidenceLocationOrigin.EdgeSource)
        };
    }

    private async Task<ApplicationResponse<T>> WithServiceAsync<T>(
        string? requestedRoot,
        CancellationToken cancellationToken,
        Func<ICodeMapGraphReader, bool, Task<ApplicationResponse<T>>> action)
    {
        try
        {
            var queryRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(requestedRoot)
                ? Directory.GetCurrentDirectory()
                : requestedRoot);
            var database = CodeMapIndexLocator.FindDatabase(queryRoot);
            var readinessFailure = await TryGuardIndexReadinessAsync<T>(database, cancellationToken);
            if (readinessFailure is not null)
            {
                return readinessFailure;
            }

            var stale = _freshness is not null
                && await CodeMapIndexLocator.IsStaleAsync(database, cancellationToken, _freshness.IsUpToDateAsync);
            await using var reader = await _graphReaderFactory(database, cancellationToken);
            return await action(reader, stale);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ApplicationResponse<T>.Failure(Classify(exception), IsStaleFailure(exception));
        }
    }

    private static IndexedSymbol? ResolveUnique(ICodeMapGraphReader service, string query, int maxResults, out QueryError? error)
    {
        var resolution = service.ResolveSymbol(query, callableOnly: false, maxResults);
        if (resolution.Matches.Count == 0)
        {
            error = new("no_matches", $"No symbol matches '{query}'.");
            return null;
        }
        if (resolution.IsAmbiguous || resolution.Matches.Count > 1)
        {
            error = new("ambiguous", $"Symbol query '{query}' is ambiguous.");
            return null;
        }
        error = null;
        return resolution.Matches[0];
    }

    private static QueryError Classify(Exception exception)
    {
        var (code, message) = CodeMapErrorClassifier.Classify(exception);
        return new QueryError(code, message);
    }

    private static bool IsStaleFailure(Exception exception)
    {
        if (exception is IndexBuildingException)
        {
            return true;
        }

        var (code, _) = CodeMapErrorClassifier.Classify(exception);
        return code is "index_building" or "index_updating" or "index_failed";
    }

    private static async Task<ApplicationResponse<T>?> TryGuardIndexReadinessAsync<T>(
        string database,
        CancellationToken cancellationToken)
    {
        var status = await CodeMapIndexStatusReader.ReadAsync(database, cancellationToken);
        var decision = IndexReadinessPolicy.Evaluate(
            status.LifecycleState,
            status.SchemaOutdated,
            status.AnalyzerVersionsOutdated);
        if (decision.CanQuery)
        {
            return null;
        }

        return ApplicationResponse<T>.Failure(
            new QueryError(decision.Error!.Code, decision.Error.Message),
            decision.Stale);
    }
}
