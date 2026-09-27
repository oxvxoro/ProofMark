using CodeMap.Core.Contracts;
using CodeMap.Core.Models;
using CodeMap.Engine.Application;
using CodeMap.Engine.Indexing;
using CodeMap.Storage;
using Proof.Adapters.CodeMap.Sessions;
using Proof.Core;

namespace Proof.Adapters.CodeMap;

public sealed class CodeMapChangeImpactProvider : IChangeImpactProvider, IChangeAnalysisProvider
{
    private readonly CodeMapAnalysisOptions? _analysisOptions;
    private readonly CodeMapApplication _application;
    private readonly Func<IncrementalCodeMapIndexer> _indexerFactory;

    public CodeMapChangeImpactProvider(
        CodeMapApplication? application = null,
        Func<IncrementalCodeMapIndexer>? indexerFactory = null)
        : this(analysisOptions: null, application, indexerFactory)
    {
    }

    public CodeMapChangeImpactProvider(
        CodeMapAnalysisOptions? analysisOptions,
        CodeMapApplication? application = null,
        Func<IncrementalCodeMapIndexer>? indexerFactory = null)
    {
        _analysisOptions = analysisOptions;
        _indexerFactory = indexerFactory ?? IncrementalCodeMapIndexerFactory.Create;
        _application = application ?? new CodeMapApplication(
            indexerFactory: _indexerFactory,
            graphReaderFactory: ImpactGraphSession.OpenGraphReaderAsync);
    }

    public async Task<ChangeImpact> AnalyzeAsync(ChangeRequest request, CancellationToken cancellationToken)
        => (await AnalyzeChangeAsync(request, cancellationToken).ConfigureAwait(false)).Impact;

    public Task<ChangeAnalysisResult> AnalyzeChangeAsync(ChangeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var lifecycle = new CodeMapAnalysisLifecycle(request, _analysisOptions, _application, _indexerFactory);
        return CodeMapAnalysisSession.RunAsync(lifecycle, cancellationToken);
    }

    internal static string IndexStampPath(string workspaceRoot)
        => ImpactIndexSession.IndexStampPath(workspaceRoot);

    internal sealed record IndexStamp(
        string SourceDigest,
        string IndexInput,
        string DatabasePath,
        long DatabaseLength,
        DateTime DatabaseWriteTimeUtc);

    internal static bool ShouldSkipIndexUpdate(string workspaceRoot, ChangeRequest request, string indexInput)
        => ImpactIndexSession.ShouldSkipIndexUpdate(workspaceRoot, request, indexInput);

    internal static IndexStamp? TryReadIndexStamp(string path)
    {
        var stamp = ImpactIndexSession.TryReadIndexStamp(path);
        return stamp is null
            ? null
            : new IndexStamp(
                stamp.SourceDigest,
                stamp.IndexInput,
                stamp.DatabasePath,
                stamp.DatabaseLength,
                stamp.DatabaseWriteTimeUtc);
    }

    internal static void WriteIndexStamp(string workspaceRoot, ChangeRequest request, string indexInput)
        => ImpactIndexSession.WriteIndexStamp(workspaceRoot, request, indexInput);

    internal static string ResolveIndexInput(string workspaceRoot, string? solutionPath)
    {
        if (string.IsNullOrWhiteSpace(solutionPath))
        {
            return Path.GetFullPath(workspaceRoot);
        }

        return Path.GetFullPath(solutionPath);
    }

    public static IReadOnlyList<string> SelectCallerSubjectIds(
        IReadOnlyList<IndexedSymbol> roots,
        Func<IndexedSymbol, IReadOnlyList<IndexedRelation>> callers)
    {
        var ids = new List<string>();
        foreach (var changed in roots)
        {
            foreach (var relation in callers(changed))
            {
                ids.Add(relation.Symbol.Id);
            }
        }

        return ids;
    }

    internal static (IReadOnlyList<RootedImpactItem> Items, bool Truncated) CollectRootedImpact(
        ICodeMapGraphReader reader,
        IReadOnlyList<IndexedSymbol> roots,
        ResolvedImpactBudget budget,
        string impactProfile)
        => ImpactWalkSession.CollectRootedImpact(reader, roots, budget, impactProfile);

    internal static ResolvedImpactBudget ResolveBudget(
        ChangeRequest request,
        IReadOnlyList<ChangedSymbolRef>? changedSymbols = null,
        ImpactAnalysisSettings? settings = null)
        => ImpactWalkSession.ResolveBudget(request, changedSymbols, settings);
}

public sealed record ResolvedImpactBudget(
    int Depth,
    int MaxResults,
    int CallerPageSize,
    int CallerMaxResults,
    double MinConfidence);
