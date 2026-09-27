using CodeMap.Core.Contracts;
using CodeMap.Core.Models;
using CodeMap.Engine.Application;
using CodeMap.Storage;
using Proof.Core;

namespace Proof.Adapters.CodeMap.Sessions;

internal interface ICodeMapAnalysisLifecycle
{
    string ResolveIndex();

    bool ShouldSkipIndexUpdate(string indexInput);

    Task AnalyzeDeletionAsync(CodeMapAnalysisState state, CancellationToken cancellationToken);

    Task UpdateHeadIndexAsync(CancellationToken cancellationToken);

    Task LocateAsync(CodeMapAnalysisState state, CancellationToken cancellationToken);

    Task<ChangeAnalysisResult> QueryHeadAsync(CodeMapAnalysisState state, CancellationToken cancellationToken);
}

internal sealed class CodeMapAnalysisState
{
    public DeletionImpactResult? Deletion { get; set; }

    public ApplicationResponse<LocateChangedSymbolsResult>? LocateResponse { get; set; }

    public bool LocateSucceeded { get; set; }

    public bool LocateHasValue { get; set; }

    public string? LocateError { get; set; }
}

internal static class CodeMapAnalysisSession
{
    internal static async Task<ChangeAnalysisResult> RunAsync(
        ICodeMapAnalysisLifecycle lifecycle,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        var indexInput = lifecycle.ResolveIndex();
        var updateSkipped = lifecycle.ShouldSkipIndexUpdate(indexInput);
        var state = new CodeMapAnalysisState();

        // 삭제 영향은 갱신 전 그래프에서 읽는다. 이 단계가 끝나기 전에는
        // head 인덱스 갱신이 실행되어서는 안 된다.
        await lifecycle.AnalyzeDeletionAsync(state, cancellationToken).ConfigureAwait(false);
        if (!updateSkipped)
        {
            await lifecycle.UpdateHeadIndexAsync(cancellationToken).ConfigureAwait(false);
        }

        await lifecycle.LocateAsync(state, cancellationToken).ConfigureAwait(false);
        if (!state.LocateSucceeded && updateSkipped)
        {
            await lifecycle.UpdateHeadIndexAsync(cancellationToken).ConfigureAwait(false);
            await lifecycle.LocateAsync(state, cancellationToken).ConfigureAwait(false);
        }

        if (!state.LocateSucceeded || !state.LocateHasValue)
        {
            throw new InvalidOperationException(state.LocateError ?? "Failed to locate changed symbols.");
        }

        return await lifecycle.QueryHeadAsync(state, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class CodeMapAnalysisLifecycle : ICodeMapAnalysisLifecycle
{
    private const string DefaultImpactProfile = "code";

    private readonly ChangeRequest _request;
    private readonly CodeMapAnalysisOptions? _analysisOptions;
    private readonly CodeMapApplication _application;
    private readonly Func<IncrementalCodeMapIndexer> _indexerFactory;
    private readonly string _workspaceRoot;
    private string _indexInput = string.Empty;
    private TestProjectClassifier? _testClassifier;

    internal CodeMapAnalysisLifecycle(
        ChangeRequest request,
        CodeMapAnalysisOptions? analysisOptions,
        CodeMapApplication application,
        Func<IncrementalCodeMapIndexer> indexerFactory)
    {
        _request = request;
        _analysisOptions = analysisOptions;
        _application = application;
        _indexerFactory = indexerFactory;
        _workspaceRoot = Path.GetFullPath(request.WorkspaceRoot);
    }

    public string ResolveIndex()
    {
        _indexInput = CodeMapChangeImpactProvider.ResolveIndexInput(_workspaceRoot, _analysisOptions?.SolutionPath);
        _testClassifier = new TestProjectClassifier(_workspaceRoot, _indexInput);
        return _indexInput;
    }

    public bool ShouldSkipIndexUpdate(string indexInput)
        => CodeMapIndexLifecycle.ShouldSkipIndexUpdate(_workspaceRoot, _request, indexInput);

    public async Task AnalyzeDeletionAsync(CodeMapAnalysisState state, CancellationToken cancellationToken)
    {
        var projects = CodeMapProjectPathMap.Load(_workspaceRoot);
        var deletedPaths = DeletionImpactAnalyzer.CollectDeletedPaths(_request.FileDeltas)
            .Select(path => CodeMapProjectPathMap.ToQueryPath(path, projects))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var deletionBudget = ImpactWalkSession.ResolveBudget(_request, settings: _analysisOptions?.Impact);
        state.Deletion = await ImpactDeletionSession.AnalyzeAsync(
            _indexInput,
            _workspaceRoot,
            _request.BaseRevision,
            deletedPaths,
            deletionBudget,
            _analysisOptions?.IndexBaseRevision == true,
            _testClassifier!.IsTestSymbol,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateHeadIndexAsync(CancellationToken cancellationToken)
    {
        await CodeMapIndexLifecycle.UpdateIndexAsync(_indexerFactory, _indexInput, cancellationToken).ConfigureAwait(false);
        CodeMapIndexLifecycle.WriteIndexStamp(_workspaceRoot, _request, _indexInput);
        _testClassifier = new TestProjectClassifier(_workspaceRoot, _indexInput);
    }

    public async Task LocateAsync(CodeMapAnalysisState state, CancellationToken cancellationToken)
    {
        var locateResponse = await ImpactChangedSymbolLocator.LocateAsync(
            _application, _workspaceRoot, _request, cancellationToken).ConfigureAwait(false);
        state.LocateResponse = locateResponse;
        state.LocateSucceeded = locateResponse.Succeeded;
        state.LocateHasValue = locateResponse.Value is not null;
        state.LocateError = locateResponse.Error?.Message;
    }

    public async Task<ChangeAnalysisResult> QueryHeadAsync(CodeMapAnalysisState state, CancellationToken cancellationToken)
    {
        var locateResponse = state.LocateResponse!;
        var database = CodeMapIndexLocator.FindDatabase(_indexInput);
        await using var reader = await ImpactGraphSession.OpenGraphReaderAsync(database, cancellationToken).ConfigureAwait(false);

        var changedSymbols = locateResponse.Value!.Symbols
            .Select(symbol => ImpactWalkSession.ToChangedSymbolRef(symbol, _testClassifier!))
            .ToArray();

        IReadOnlyList<ApiCompatibilityFact> facts = [];
        if (_analysisOptions is not null)
        {
            var currentEntries = ImpactBaselineSession.CaptureEntries(reader);
            var baseline = await ImpactBaselineSession.LoadAsync(
                _analysisOptions,
                _workspaceRoot,
                _request,
                changedSymbols
                    .Concat(state.Deletion?.DeletedSymbols ?? [])
                    .ToArray(),
                currentEntries,
                cancellationToken).ConfigureAwait(false);
            facts = CodeMapPublicSurfaceAnalyzer.Analyze(
                baseline.Fingerprints,
                PublicSurfaceSnapshotStore.ToFingerprints(currentEntries),
                baseline.Entries,
                currentEntries);
        }

        var budget = ImpactWalkSession.ResolveBudget(_request, changedSymbols, _analysisOptions?.Impact);
        var roots = locateResponse.Value.Symbols.ToArray();
        var impactProfile = string.IsNullOrWhiteSpace(_analysisOptions?.Impact?.Profile)
            ? DefaultImpactProfile
            : _analysisOptions!.Impact.Profile;

        var impact = await ImpactWalkSession.BuildAsync(
            _request,
            roots,
            changedSymbols,
            state.Deletion,
            reader,
            budget,
            impactProfile,
            locateResponse.Value.LocationUnknownSpans.Count,
            _testClassifier!,
            _analysisOptions?.RunArchitectureCheck == true,
            _workspaceRoot,
            database,
            cancellationToken).ConfigureAwait(false);
        return new ChangeAnalysisResult(impact, facts);
    }
}
