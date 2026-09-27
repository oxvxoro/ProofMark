using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json;
using CodeMap.CSharp;
using CodeMap.Core;
using CodeMap.Core.Contracts;
using CodeMap.Core.Models;
using CodeMap.Core.Models.Investigation;
using CodeMap.Engine.Application;
using CodeMap.Engine.Application.Investigation;
using CodeMap.Storage;
using CodeMap.Storage.Queries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CodeMap.Mcp;

public static class CodeMapMcpServer
{
    private static string DefaultRoot { get; set; } = Directory.GetCurrentDirectory();

    public static async Task RunAsync(string defaultRoot, CancellationToken cancellationToken)
    {
        DefaultRoot = defaultRoot;
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.AddConsole(consoleLogOptions =>
            consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.AddSingleton(new CodeMapMcpContext(defaultRoot));
        builder.Services.AddSingleton<IIndexFreshnessService>(services =>
            services.GetRequiredService<CodeMapMcpContext>());
        builder.Services.AddSingleton<CodeMapApplication>(services =>
            CreateApplication(services.GetRequiredService<IIndexFreshnessService>()));
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();
        await builder.Build().RunAsync(cancellationToken);
    }

    internal static CodeMapApplication CreateApplication(IIndexFreshnessService freshness) =>
        new(freshness, graphReaderFactory: OpenGraphReaderAsync);

    private static async Task<ICodeMapGraphReader> OpenGraphReaderAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        var connection = await new CodeMapQueryStore(databasePath)
            .OpenReadOnlyConnectionAsync(cancellationToken);
        return new SqliteCodeMapGraphReader(connection);
    }
}

















public sealed class CodeMapMcpContext(string defaultRoot) : IDisposable, IIndexFreshnessService
{
    public string DefaultRoot { get; } = defaultRoot;

    internal McpWorkspacePolicy WorkspacePolicy { get; } =
        new McpWorkspacePolicy(defaultRoot, Directory.GetCurrentDirectory());

    private readonly ConcurrentDictionary<string, FreshnessEntry> _freshness = new(StringComparer.OrdinalIgnoreCase);
    internal SemaphoreSlim SemanticSliceGate { get; } = new(1, 1);






    internal Func<string, FileSystemWatcher> WatcherFactory { private get; init; } = root => new FileSystemWatcher(root)
    {
        IncludeSubdirectories = true,
        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
    };

    internal Func<string, CancellationToken, Task<bool>> FreshnessProbe { private get; init; } =
        (root, cancellationToken) => CodeMap.Engine.Indexing.IncrementalCodeMapIndexerFactory.Create().IsUpToDateAsync(root, cancellationToken);







    public void Dispose()
    {
        foreach (var entry in _freshness.Values)
            entry.Watcher?.Dispose();
        SemanticSliceGate.Dispose();
    }

    public async Task<bool> IsUpToDateAsync(string projectRoot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = Path.GetFullPath(projectRoot);
        var entry = _freshness.GetOrAdd(key, _ => new FreshnessEntry());

        Task<bool> probeTask;
        lock (entry.Gate)
        {
            if (entry.UpToDate is { } cached)
                return cached;




            // 해시 검사 중 발생한 변경도 놓치지 않도록 watcher를 먼저 시작한다.
            EnsureWatcher(key, entry);
            entry.InFlight ??= ProbeAndCacheAsync(key, entry);
            probeTask = entry.InFlight;
        }

        return await probeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }


    public void InvalidateRoot(string projectRoot) => Invalidate(Path.GetFullPath(projectRoot));

    void IIndexFreshnessService.Invalidate(string projectRoot) => InvalidateRoot(projectRoot);

    private void Invalidate(string key)
    {
        if (_freshness.TryGetValue(key, out var entry))
            lock (entry.Gate)
            {
                entry.UpToDate = null;
                entry.Generation++;
            }
    }

    private void EnsureWatcher(string root, FreshnessEntry entry)
    {
        if (entry.Watcher is not null)
            return;
        try
        {
            var watcher = WatcherFactory(root);
            void OnEvent(object? _, FileSystemEventArgs e)
            {
                if (!IgnoreRules.IsIgnored(root, e.FullPath) && !IgnoreRules.IsOutsideRoot(root, e.FullPath))
                    Invalidate(root);
            }
            watcher.Changed += OnEvent;
            watcher.Created += OnEvent;
            watcher.Deleted += OnEvent;



            watcher.Renamed += (_, e) =>
            {
                if (!IgnoreRules.IsIgnored(root, e.OldFullPath) && !IgnoreRules.IsOutsideRoot(root, e.OldFullPath))
                    Invalidate(root);
                else
                    OnEvent(null, e);
            };
            watcher.Error += (_, _) =>
            {
                lock (entry.Gate)
                {
                    entry.UpToDate = null;
                    entry.Generation++;
                    entry.Watcher?.Dispose();
                    entry.Watcher = null;
                }
            };
            watcher.EnableRaisingEvents = true;
            entry.Watcher = watcher;
        }
        catch
        {




        }
    }

    private async Task<bool> ProbeAndCacheAsync(string root, FreshnessEntry entry)
    {
        int generation;
        lock (entry.Gate)
            generation = entry.Generation;
        try
        {
            var upToDate = await FreshnessProbe(root, CancellationToken.None).ConfigureAwait(false);
            lock (entry.Gate)
            {
                if (entry.Generation == generation && entry.Watcher is not null)
                    entry.UpToDate = upToDate;
                return entry.Generation == generation && upToDate;
            }
        }
        finally
        {
            lock (entry.Gate)
                entry.InFlight = null;
        }
    }

    private sealed class FreshnessEntry
    {
        public readonly object Gate = new();
        public bool? UpToDate;
        public int Generation;
        public Task<bool>? InFlight;
        public FileSystemWatcher? Watcher;
    }
}

[McpServerToolType]
public static class CodeMapTools
{
    [McpServerTool, Description("Find symbols in the CodeMap semantic index.")]
    public static async Task<string> FindSymbol(CodeMapMcpContext context, CodeMapApplication application, string query, string? root = null, int maxResults = 20, CancellationToken cancellationToken = default)
    {
        if (RejectRootIfNeeded(context, root, out var projectRoot) is { } rejectedRoot)
        {
            return rejectedRoot;
        }

        var response = await application.FindAsync(new FindRequest(query, projectRoot, maxResults), cancellationToken);
        if (!response.Succeeded)
            return JsonSerializer.Serialize(new { error = ErrorObject(response.Error!), stale = response.Stale });
        var matches = response.Value!.Matches;
        return JsonSerializer.Serialize(new
        {
            matches = matches.Select(symbol => new
            {
                symbol.Id,
                symbol.Project,
                kind = symbol.Kind.ToString(),
                symbol.Name,
                symbol.QualifiedName,
                file = symbol.RelativePath,
                symbol.StartLine,
                symbol.EndLine,
                symbol.Language
            }),
            stale = response.Stale
        });
    }

    [McpServerTool, Description("Build an agent-friendly context bundle for a task.")]
    public static async Task<string> GetContext(CodeMapMcpContext context, CodeMapApplication application, string task, string? root = null, int maxResults = 5, int tokens = 500, CancellationToken cancellationToken = default)
    {
        if (RejectRootIfNeeded(context, root, out var projectRoot) is { } rejectedRoot)
        {
            return rejectedRoot;
        }

        var response = await application.ContextAsync(new ContextRequest(task, projectRoot, maxResults, tokens), cancellationToken);
        if (!response.Succeeded)
            return JsonSerializer.Serialize(new { error = ErrorObject(response.Error!), stale = response.Stale });
        return JsonSerializer.Serialize(new
        {
            matches = response.Value!.Matches,
            mapLines = response.Value.Map.Lines,
            stale = response.Stale
        });
    }

    [McpServerTool, Description("Run a goal-directed investigation that internally combines find, callers, callees, impact, flow, and slice into one deterministic, budget-aware evidence bundle. Prefer this when the root symbol and goal are already known; use explain_relation separately when a specific edge evidence string is required.")]
    public static async Task<string> Investigate(
        CodeMapMcpContext context,
        CodeMapApplication application,
        string query,
        string goal,
        string? root = null,
        int tokens = 2000,
        int maxResults = 200,
        int? depth = null,
        double minConfidence = 0,
        bool includeHeuristic = true,
        string sourceMode = "minimal",
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<InvestigationGoal>(goal, true, out var parsedGoal))
            return InvestigationPresentationMapper.Serialize(
                new InvestigationErrorResponseDto(1, query, goal,
                    new InvestigationErrorDto("query_failed", "goal must be one of: debug, trace, impact, understand."),
                    "query_failed", false));
        if (RejectRootIfNeeded(context, root, out var projectRoot) is { } rejectedRoot)
        {
            return rejectedRoot;
        }

        var response = await application.InvestigateAsync(
            new InvestigationRequest(query, parsedGoal, projectRoot, tokens, maxResults, depth, minConfidence, includeHeuristic, sourceMode),
            cancellationToken);
        if (!response.Succeeded)
        {
            if (response.Error!.Code == "ambiguous" && response.Value is { } ambiguous)
                return InvestigationPresentationMapper.Serialize(
                    InvestigationPresentationMapper.ToAmbiguousResponse(query, parsedGoal, ambiguous, response.Stale));
            return InvestigationPresentationMapper.Serialize(
                InvestigationPresentationMapper.ToError(query, parsedGoal.ToString().ToLowerInvariant(), response.Error, response.Stale));
        }
        var result = response.Value!;
        return InvestigationPresentationMapper.Serialize(
            InvestigationPresentationMapper.ToResponse(query, parsedGoal, result, response.Stale));

    }

    [McpServerTool, Description("Compute an intraprocedural C# semantic dependency slice for one executable symbol. Requires a fresh CodeMap index; use refresh_index first when the index is stale.")]
    public static async Task<string> GetSemanticSlice(
        CodeMapMcpContext context,
        CodeMapApplication application,
        string query,
        string direction = "backward",
        int? line = null,
        int? column = null,
        int maxResults = 80,
        bool includeSource = false,
        string? root = null,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<SliceDirection>(direction, ignoreCase: true, out var parsedDirection))
            return JsonSerializer.Serialize(new { error = new { code = "query_failed", message = "direction must be 'backward' or 'forward'." } });

        if (RejectRootIfNeeded(context, root, out var projectRoot) is { } rejectedRoot)
        {
            return rejectedRoot;
        }

        var response = await application.SemanticSliceAsync(
            projectRoot,
            new SemanticSliceRequest(parsedDirection, line, column, maxResults, query, includeSource),
            cancellationToken);
        if (!response.Succeeded)
            return JsonSerializer.Serialize(new { error = ErrorObject(response.Error!), stale = response.Stale });
        var result = response.Value!;
        return JsonSerializer.Serialize(new
        {
            version = 1,
            query,
            direction = parsedDirection.ToString().ToLowerInvariant(),
            entrySymbol = result.EntrySymbol,
            scope = result.Scope,
            items = result.Items,
            dependencies = result.Dependencies,
            result.Truncated,
            source = result.Source,
            stale = response.Stale
        });
    }

    [McpServerTool, Description("Show reverse dependency impact for a symbol. profile 'code' (default) follows code references only; 'app' additionally reuses the routes/DI/UI edges flow already traverses.")]
    public static async Task<string> GetImpact(CodeMapMcpContext context, CodeMapApplication application, string query, string? root = null, int depth = 2, int maxResults = 20, string profile = "code", CancellationToken cancellationToken = default)
    {
        if (RejectRootIfNeeded(context, root, out var projectRoot) is { } rejectedRoot)
        {
            return rejectedRoot;
        }

        var response = await application.ImpactAsync(
            new ImpactRequest(query, projectRoot, depth, maxResults, profile), cancellationToken);
        if (!response.Succeeded)
            return JsonSerializer.Serialize(new { error = ErrorObject(response.Error!), query, stale = response.Stale });
        return JsonSerializer.Serialize(new
        {
            symbol = response.Value!.Symbol,
            impact = response.Value.Items,
            stale = response.Stale
        });
    }

    [McpServerTool, Description("Explain relations between two symbols with evidence metadata. Preferred follow-up after get_flow(includeEvidence=false) to prove the one selected edge, rather than requesting evidence for every relation up front.")]
    public static async Task<string> ExplainRelation(CodeMapMcpContext context, CodeMapApplication application, string source, string target, string? root = null, double minConfidence = 0, int maxResults = 20, CancellationToken cancellationToken = default)
    {
        if (RejectRootIfNeeded(context, root, out var projectRoot) is { } rejectedRoot)
        {
            return rejectedRoot;
        }

        var response = await application.RelationsAsync(
            new RelationsRequest(source, target, projectRoot, null, maxResults, minConfidence), cancellationToken);
        if (!response.Succeeded)
            return JsonSerializer.Serialize(new
            {
                error = ErrorObject(response.Error!),
                query = ErrorQuery(response.Error!, source, target),
                stale = response.Stale
            });
        var relations = response.Value!
            .Select(item => new
            {
                kind = "relation",
                source = item.Source.DisplayName,
                target = item.Target.DisplayName,
                sourceId = item.Source.Id,
                targetId = item.Target.Id,
                edgeKind = item.Edge.Kind.ToString(),
                resolutionKind = item.Edge.ResolutionKind.ToString().ToLowerInvariant(),
                confidence = item.Edge.Confidence,
                location = item.Evidence.File is null && item.Evidence.Line is null
                    ? null
                    : new { file = item.Evidence.File, line = item.Evidence.Line },
                evidence = item.Evidence.Evidence
            });
        return JsonSerializer.Serialize(new { relations, stale = response.Stale });
    }

    [McpServerTool, Description("Traverse HTTP/UI application flow edges (routes, DI, Razor/Blazor, WPF XAML) from an entry symbol or route. For first-pass discovery, prefer includeEvidence=false with the smallest useful depth/maxResults, then call explain_relation for the one edge that needs proof.")]





    public static async Task<string> GetFlow(CodeMapMcpContext context, CodeMapApplication application, string entry, string kind = "all", int depth = 4, string? root = null, int maxResults = 20, double minConfidence = 0, CancellationToken cancellationToken = default, bool includeEvidence = true)
    {
        if (RejectRootIfNeeded(context, root, out var projectRoot) is { } rejectedRoot)
        {
            return rejectedRoot;
        }

        var response = await application.FlowAsync(
            new FlowRequest(entry, kind, depth, projectRoot, maxResults, minConfidence), cancellationToken);
        if (!response.Succeeded)
            return JsonSerializer.Serialize(new { error = ErrorObject(response.Error!), query = entry, stale = response.Stale });
        var flowResult = response.Value!;
        var entrySymbol = flowResult.Entry;
        var flow = flowResult.Items;
        var sourceById = flowResult.Sources;






        object relations;
        if (includeEvidence)
        {
            relations = flow.Select(item =>
            {
                var sourceSymbol = sourceById.GetValueOrDefault(item.Via.SourceId) ?? entrySymbol;
                var evidenceValue = RelationEvidenceMapper.FromEdge(item.Via, sourceSymbol, item.Symbol,
                    item.Via.SourceFileId is not null && flowResult.Files.TryGetValue(item.Via.SourceFileId, out var file) ? file.RelativePath : null,
                    item.Via.Line);
                return new
                {
                    kind = "flow",
                    source = sourceSymbol.DisplayName,
                    target = item.Symbol.DisplayName,
                    sourceId = sourceSymbol.Id,
                    targetId = item.Symbol.Id,
                    depth = item.Depth,
                    edgeKind = item.Via.Kind.ToString(),
                    resolutionKind = item.Via.ResolutionKind.ToString().ToLowerInvariant(),
                    confidence = item.Via.Confidence,
                    location = evidenceValue.File is null && evidenceValue.Line is null
                        ? null
                        : new { file = evidenceValue.File, line = evidenceValue.Line },
                    evidence = evidenceValue.Evidence
                };
            }).ToArray();
        }
        else
        {
            relations = flow.Select(item =>
            {
                var sourceSymbol = sourceById.GetValueOrDefault(item.Via.SourceId) ?? entrySymbol;
                return new
                {
                    kind = "flow",
                    source = sourceSymbol.DisplayName,
                    target = item.Symbol.DisplayName,
                    sourceId = sourceSymbol.Id,
                    targetId = item.Symbol.Id,
                    depth = item.Depth,
                    edgeKind = item.Via.Kind.ToString(),
                    resolutionKind = item.Via.ResolutionKind.ToString().ToLowerInvariant(),
                    confidence = item.Via.Confidence
                };
            }).ToArray();
        }
        return JsonSerializer.Serialize(new
        {
            matches = new[]
            {
                new
                {
                    entrySymbol.Id,
                    entrySymbol.Project,
                    kind = entrySymbol.Kind.ToString(),
                    entrySymbol.Name,
                    entrySymbol.QualifiedName,
                    file = entrySymbol.RelativePath,
                    entrySymbol.StartLine,
                    entrySymbol.EndLine,
                    entrySymbol.Language
                }
            },
            relations,
            stale = response.Stale
        });
    }

    [McpServerTool, Description("Refresh the CodeMap index for the repository.")]
    public static async Task<string> RefreshIndex(CodeMapMcpContext context, CodeMapApplication application, string? root = null, bool force = false, CancellationToken cancellationToken = default)
    {
        if (RejectRootIfNeeded(context, root, out var requestedRoot) is { } rejectedRoot)
        {
            return rejectedRoot;
        }






        string actualRoot;
        try
        {
            actualRoot = ResolveIndexRoot(requestedRoot);
        }
        catch (FileNotFoundException)
        {
            actualRoot = requestedRoot;
        }
        var response = await application.RefreshIndexAsync(new RefreshIndexRequest(actualRoot, force), cancellationToken);
        return response.Succeeded
            ? response.Value!.ToString()
            : JsonSerializer.Serialize(new { error = ErrorObject(response.Error!) });
    }

    [McpServerTool, Description("Read CodeMap index metadata and counts without modifying the index.")]
    public static async Task<string> GetStatus(
        CodeMapMcpContext context,
        CodeMapApplication application,
        string? root = null,
        bool checkFreshness = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (RejectRootIfNeeded(context, root, out var projectRoot) is { } rejectedRoot)
            {
                return rejectedRoot;
            }

            var response = await application.StatusAsync(new StatusRequest(projectRoot, checkFreshness), cancellationToken);
            if (!response.Succeeded)
                return JsonSerializer.Serialize(new { version = 1, error = ErrorObject(response.Error!) });
            var status = response.Value!;
            return JsonSerializer.Serialize(new
            {
                version = 1,
                indexState = status.IndexState,
                lastIndexedAtUtc = status.LastIndexedAtUtc,
                schemaVersion = status.SchemaVersion,
                schemaOutdated = status.SchemaOutdated,
                analyzerVersions = status.AnalyzerVersions,
                analyzerVersionsOutdated = status.AnalyzerVersionsOutdated,
                symbols = status.Symbols,
                edges = status.Edges,
                freshnessChecked = checkFreshness,
                stale = checkFreshness ? response.Stale : (bool?)null
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var (code, message) = CodeMapErrorClassifier.Classify(exception);
            return JsonSerializer.Serialize(new { version = 1, error = new { code, message } });
        }
    }

    [McpServerTool, Description("Show callers of a callable symbol.")]
    public static Task<string> GetCallers(CodeMapMcpContext context, CodeMapApplication application, string query, string? root = null, int maxResults = 20, double minConfidence = 0, CancellationToken cancellationToken = default)
    {
        if (RejectRootIfNeeded(context, root, out var projectRoot) is { } rejectedRoot)
        {
            return Task.FromResult(rejectedRoot);
        }

        return SerializeRelationList(application, new SymbolRelationRequest(query, projectRoot, maxResults, MinConfidence: minConfidence), application.CallersAsync, cancellationToken);
    }

    [McpServerTool, Description("Show callees of a callable symbol.")]
    public static Task<string> GetCallees(CodeMapMcpContext context, CodeMapApplication application, string query, string? root = null, int depth = 1, int maxResults = 20, double minConfidence = 0, CancellationToken cancellationToken = default)
    {
        if (RejectRootIfNeeded(context, root, out var projectRoot) is { } rejectedRoot)
        {
            return Task.FromResult(rejectedRoot);
        }

        return SerializeRelationList(application, new SymbolRelationRequest(query, projectRoot, maxResults, depth, MinConfidence: minConfidence), application.CalleesAsync, cancellationToken);
    }

    [McpServerTool, Description("Show references and implemented-by relations for a symbol.")]
    public static Task<string> GetRefs(CodeMapMcpContext context, CodeMapApplication application, string query, string? root = null, int maxResults = 20, double minConfidence = 0, CancellationToken cancellationToken = default)
    {
        if (RejectRootIfNeeded(context, root, out var projectRoot) is { } rejectedRoot)
        {
            return Task.FromResult(rejectedRoot);
        }

        return SerializeRelationList(application, new SymbolRelationRequest(query, projectRoot, maxResults, MinConfidence: minConfidence), application.RefsAsync, cancellationToken);
    }

    [McpServerTool, Description("Show implementations of a type or interface.")]
    public static Task<string> GetImpl(CodeMapMcpContext context, CodeMapApplication application, string query, string? root = null, int maxResults = 20, double minConfidence = 0, CancellationToken cancellationToken = default)
    {
        if (RejectRootIfNeeded(context, root, out var projectRoot) is { } rejectedRoot)
        {
            return Task.FromResult(rejectedRoot);
        }

        return SerializeRelationList(application, new SymbolRelationRequest(query, projectRoot, maxResults, MinConfidence: minConfidence), application.ImplAsync, cancellationToken);
    }

    [McpServerTool, Description("Analyze impact for git-changed files instead of a single symbol.")]
    public static async Task<string> GetChangedImpact(CodeMapMcpContext context, CodeMapApplication application, string? root = null, string? baseRevision = null, int depth = 2, int maxResults = 20, string profile = "code", double minConfidence = 0, CancellationToken cancellationToken = default)
    {
        if (RejectRootIfNeeded(context, root, out var projectRoot) is { } rejectedRoot)
        {
            return rejectedRoot;
        }

        var response = await application.ChangedImpactAsync(
            new ChangedImpactRequest(projectRoot, baseRevision, depth, maxResults, profile, minConfidence),
            cancellationToken);
        if (!response.Succeeded)
            return JsonSerializer.Serialize(new { error = ErrorObject(response.Error!), stale = response.Stale });
        return JsonSerializer.Serialize(new
        {
            changedFiles = response.Value!.ChangedFiles,
            directSymbols = response.Value.DirectSymbols,
            impact = response.Value.Impact,
            analysisComplete = response.Value.AnalysisComplete,
            unresolvableChangedPaths = response.Value.UnresolvableChangedPaths,
            stale = response.Stale
        });
    }

    private static async Task<string> SerializeRelationList(
        CodeMapApplication application,
        SymbolRelationRequest request,
        Func<SymbolRelationRequest, CancellationToken, Task<ApplicationResponse<ApplicationRelationListResult>>> query,
        CancellationToken cancellationToken)
    {
        var response = await query(request, cancellationToken);
        if (!response.Succeeded)
            return JsonSerializer.Serialize(new { error = ErrorObject(response.Error!), query = request.Query, stale = response.Stale });
        return JsonSerializer.Serialize(new
        {
            symbol = response.Value!.Symbol,
            relations = response.Value.Relations,
            stale = response.Stale
        });
    }

    // 도구 메서드를 직접 호출하는 쪽을 위한 소스 호환 오버로드.
    // MCP 검색은 위의 특성이 붙은 오버로드를 쓰고, 호스트 컨테이너에서
    // 장기 실행 애플리케이션 인스턴스를 주입한다.
    public static async Task<string> FindSymbol(CodeMapMcpContext context, string query, string? root = null, int maxResults = 20, CancellationToken cancellationToken = default)
    {
        var response = await FindSymbol(context, CodeMapMcpServer.CreateApplication(context), query, root, maxResults, cancellationToken);
        using var document = JsonDocument.Parse(response);
        if (document.RootElement.TryGetProperty("error", out var error)
            && error.ValueKind == JsonValueKind.Object
            && error.TryGetProperty("code", out var code)
            && code.GetString() == "schema_outdated")
            throw new InvalidOperationException(error.GetProperty("message").GetString());
        return response;
    }

    public static Task<string> GetContext(CodeMapMcpContext context, string task, string? root = null, int maxResults = 5, int tokens = 500, CancellationToken cancellationToken = default) =>
        GetContext(context, CodeMapMcpServer.CreateApplication(context), task, root, maxResults, tokens, cancellationToken);

    public static Task<string> GetSemanticSlice(CodeMapMcpContext context, string query, string direction = "backward", int? line = null, int? column = null, int maxResults = 80, bool includeSource = false, string? root = null, CancellationToken cancellationToken = default) =>
        GetSemanticSlice(context, CodeMapMcpServer.CreateApplication(context), query, direction, line, column, maxResults, includeSource, root, cancellationToken);

    public static Task<string> GetImpact(CodeMapMcpContext context, string query, string? root = null, int depth = 2, int maxResults = 20, string profile = "code", CancellationToken cancellationToken = default) =>
        LegacyErrorResponse(GetImpact(context, CodeMapMcpServer.CreateApplication(context), query, root, depth, maxResults, profile, cancellationToken));

    public static Task<string> ExplainRelation(CodeMapMcpContext context, string source, string target, string? root = null, double minConfidence = 0, int maxResults = 20, CancellationToken cancellationToken = default) =>
        LegacyErrorResponse(ExplainRelation(context, CodeMapMcpServer.CreateApplication(context), source, target, root, minConfidence, maxResults, cancellationToken));

    public static Task<string> GetFlow(CodeMapMcpContext context, string entry, string kind = "all", int depth = 4, string? root = null, int maxResults = 20, double minConfidence = 0, CancellationToken cancellationToken = default, bool includeEvidence = true) =>
        LegacyErrorResponse(GetFlow(context, CodeMapMcpServer.CreateApplication(context), entry, kind, depth, root, maxResults, minConfidence, cancellationToken, includeEvidence));

    public static Task<string> RefreshIndex(CodeMapMcpContext context, string? root = null, bool force = false, CancellationToken cancellationToken = default) =>
        RefreshIndex(context, CodeMapMcpServer.CreateApplication(context), root, force, cancellationToken);

    public static Task<string> GetStatus(CodeMapMcpContext context, string? root = null, bool checkFreshness = false, CancellationToken cancellationToken = default) =>
        GetStatus(context, CodeMapMcpServer.CreateApplication(context), root, checkFreshness, cancellationToken);

    private static async Task<string> LegacyErrorResponse(Task<string> responseTask)
    {
        var response = await responseTask;
        using var document = JsonDocument.Parse(response);
        if (!document.RootElement.TryGetProperty("error", out var error)
            || error.ValueKind != JsonValueKind.Object
            || !(error.TryGetProperty("code", out var code) || error.TryGetProperty("Code", out code)))
            return response;
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
            values[property.Name] = property.NameEquals("error") ? code.GetString() : property.Value;
        return JsonSerializer.Serialize(values);
    }

    private static object ErrorObject(QueryError error) =>
        new { code = error.Code, message = error.Message };

    private static string ErrorQuery(QueryError error, string first, string second) =>
        error.Message.StartsWith("Symbol query '", StringComparison.Ordinal)
            ? error.Message[14..].Split('\'', 2)[0]
            : error.Message.Contains(second, StringComparison.Ordinal) ? second : first;

    private static string? RejectRootIfNeeded(CodeMapMcpContext context, string? root, out string projectRoot)
    {
        if (TryResolveProjectRoot(context, root, out projectRoot, out var rejection))
        {
            return null;
        }

        return rejection;
    }

    private static bool TryResolveProjectRoot(CodeMapMcpContext context, string? root, out string projectRoot, out string rejection)
    {
        rejection = string.Empty;
        if (context.WorkspacePolicy.TryResolve(root, out projectRoot, out var error))
        {
            return true;
        }

        rejection = error!;
        projectRoot = string.Empty;
        return false;
    }

    private static string ResolveIndexRoot(string queryRoot)
    {
        var databasePath = CodeMapIndexLocator.FindDatabase(queryRoot);
        return CodeMapIndexLocator.ResolveIndexRoot(databasePath);
    }

}
