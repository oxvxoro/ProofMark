using CodeMap.Core.Models;
using CodeMap.CSharp;
using CodeMap.Engine;
using CodeMap.Web;
using System.Text.Json;

namespace CodeMap.Storage;


public sealed partial class IncrementalCodeMapIndexer
{
    private readonly CSharpWorkspaceIndexer _workspaceIndexer;
    private readonly WebWorkspaceIndexer _webWorkspaceIndexer;

    public IncrementalCodeMapIndexer(CSharpWorkspaceIndexer workspaceIndexer, WebWorkspaceIndexer webWorkspaceIndexer)
    {
        ArgumentNullException.ThrowIfNull(workspaceIndexer);
        ArgumentNullException.ThrowIfNull(webWorkspaceIndexer);
        _workspaceIndexer = workspaceIndexer;
        _webWorkspaceIndexer = webWorkspaceIndexer;
    }

    private IReadOnlyDictionary<string, string[]> _lastProjectReferences = new Dictionary<string, string[]>(StringComparer.Ordinal);







    private IReadOnlyDictionary<string, string?> _lastPublicSurfaceFingerprints = new Dictionary<string, string?>(StringComparer.Ordinal);









    private IReadOnlyDictionary<string, IReadOnlyList<ExternalAssemblyReference>> _lastExternalAssembliesByOwningProject =
        new Dictionary<string, IReadOnlyList<ExternalAssemblyReference>>(StringComparer.Ordinal);

    public async Task<IndexSummary> IndexAsync(string inputPath, bool force = false, CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var root = ResolveRoot(inputPath, out var resolved);
        var store = new SqliteCodeMapStore(Path.Combine(root, ".codemap", "index.db"));
        var scipImports = await ResolveScipImportsAsync(root, throwOnStale: force, cancellationToken);
        if (force)
            WipeIndexArtifacts(root, store.DatabasePath);

        var projects = await AnalyzeAllAsync(root, resolved, csharpProjectNames: null, dirtyProjectNames: null, cancellationToken);
        var previousProjects = force || !File.Exists(store.DatabasePath)
            ? new HashSet<string>(StringComparer.Ordinal)
            : (await store.GetFilesAsync(cancellationToken))
                .Select(file => file.Project)
                .ToHashSet(StringComparer.Ordinal);
        var currentProjects = projects.Select(project => project.ProjectName).ToHashSet(StringComparer.Ordinal);
        await store.ReplaceProjectsAsync(projects, previousProjects.Except(currentProjects, StringComparer.Ordinal).ToArray(), cancellationToken);
        var counts = await store.GetCountsAsync(cancellationToken);





        var files = projects.Where(p => !SqliteCodeMapStore.IsExternalProject(p.ProjectName)).Sum(p => p.Files.Count);
        await WriteStateAsync(root, resolved, projects, previousState: null, removedProjects: null, _lastProjectReferences, _lastPublicSurfaceFingerprints, _lastExternalAssembliesByOwningProject, cancellationToken);
        foreach (var (name, artifact) in scipImports)
            await new ScipImportService().ImportAsync(root, artifact, new ScipImportOptions(name), cancellationToken);
        counts = await store.GetCountsAsync(cancellationToken);
        stopwatch.Stop();
        return new IndexSummary(files, files, 0, 0, 0, counts.Symbols, counts.Edges, stopwatch.Elapsed,
            projects.Select(project => project.ProjectName).ToArray());
    }

    public async Task<IndexSummary> UpdateAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var root = ResolveRoot(inputPath, out var resolved);
        var store = new SqliteCodeMapStore(Path.Combine(root, ".codemap", "index.db"));
        var hadDatabase = File.Exists(store.DatabasePath);





        if (hadDatabase)
            await EnsureAnalyzerVersionsAsync(store.DatabasePath, cancellationToken);
        var cachedState = hadDatabase
            ? await TryLoadUpToDateStateAsync(root, resolved, cancellationToken)
            : null;
        if (cachedState is not null)
        {
            var cachedCounts = await store.GetCountsAsync(cancellationToken);
            var cachedFileCount = cachedState.Projects.Sum(p => p.Files.Count);
            stopwatch.Stop();
            return new IndexSummary(cachedFileCount, 0, 0, 0, cachedFileCount, cachedCounts.Symbols, cachedCounts.Edges, stopwatch.Elapsed);
        }

        var previousState = await TryLoadStateAsync(root, cancellationToken);
        if (previousState is not null)
        {
            // 선언되고 산출물이 있는 스테일 SCIP는 끝의 ImportDeclaredScipProvidersAsync가 다시 읽는다.
            var declared = await ReadDeclaredScipProvidersAsync(root, cancellationToken);
            foreach (var project in previousState.Projects.Where(project => string.Equals(project.ProviderKind, "scip", StringComparison.Ordinal)))
                if (!await IsScipProjectUpToDateAsync(project, cancellationToken)
                    && !declared.Any(item => item.Exists && string.Equals("scip:" + item.Name, project.ProjectName, StringComparison.Ordinal)))
                    throw new InvalidOperationException($"SCIP provider '{project.ProjectName}' is stale. Re-import its artifact before running update.");
        }
        if (previousState is null || !hadDatabase)
        {
            var fullProjects = await AnalyzeAllAsync(root, resolved, csharpProjectNames: null, dirtyProjectNames: null, cancellationToken);
            var previousProjects = (await store.GetFilesAsync(cancellationToken))
                .Select(file => file.Project)
                .ToHashSet(StringComparer.Ordinal);
            var currentProjects = fullProjects.Select(project => project.ProjectName).ToHashSet(StringComparer.Ordinal);
            await store.ReplaceProjectsAsync(fullProjects, previousProjects.Except(currentProjects, StringComparer.Ordinal).ToArray(), cancellationToken);
            var fullFiles = fullProjects.Where(p => !SqliteCodeMapStore.IsExternalProject(p.ProjectName)).Sum(project => project.Files.Count);
            await WriteStateAsync(root, resolved, fullProjects, previousState: null, removedProjects: null, _lastProjectReferences, _lastPublicSurfaceFingerprints, _lastExternalAssembliesByOwningProject, cancellationToken);
            await ImportDeclaredScipProvidersAsync(root, cancellationToken);
            var fullCounts = await store.GetCountsAsync(cancellationToken);
            stopwatch.Stop();
            return new IndexSummary(fullFiles, fullFiles, 0, 0, 0, fullCounts.Symbols, fullCounts.Edges, stopwatch.Elapsed,
                fullProjects.Select(project => project.ProjectName).ToArray());
        }

        if (resolved is not null && IsSolutionFile(resolved)
            && await IsSolutionInputChangedAsync(previousState, resolved, cancellationToken))
        {
            return await ReconcileSolutionMembershipAsync(root, resolved, store, previousState, stopwatch, cancellationToken);
        }

        var changeSummary = await DetectProjectChangesAsync(previousState, root, resolved, cancellationToken);
        var dirtyProjects = new HashSet<string>(changeSummary.DirtyProjects, StringComparer.Ordinal);
        IReadOnlyList<AnalyzedProject> analyzedProjects;
        var skippedPropagationNotes = new List<string>();
        if (resolved is null)
        {
            analyzedProjects = await AnalyzeAllAsync(root, resolved, csharpProjectNames: null, dirtyProjectNames: dirtyProjects, cancellationToken);
        }
        else
        {
            IReadOnlyDictionary<string, HashSet<string>> referencing;
            CSharpWorkspaceAnalysisResult? firstWaveSeed = null;
            if (HasStoredProjectGraph(previousState) && !ShouldRefreshProjectGraph(previousState, changeSummary))
                referencing = BuildReverseReferencing(previousState.Projects);
            else
            {






                firstWaveSeed = await _workspaceIndexer.AnalyzeWithReferencingGraphAsync(
                    resolved,
                    dirtyProjects,
                    cancellationToken);
                referencing = firstWaveSeed.ReferencingProjects;
                _lastProjectReferences = firstWaveSeed.ProjectReferences;
            }
            analyzedProjects = await ExpandAndAnalyzeUsingFingerprintsAsync(
                root, resolved, dirtyProjects, referencing, previousState, cancellationToken, firstWaveSeed, skippedPropagationNotes);
        }

        var analyzedNames = analyzedProjects.Select(project => project.ProjectName).ToHashSet(StringComparer.Ordinal);
        var orphanedExternalProjects = ComputeOrphanedExternalProjects(
            previousState, changeSummary.RemovedProjects, analyzedProjects, _lastExternalAssembliesByOwningProject);
        var deletedProjects = changeSummary.RemovedProjects
            .Where(project => !analyzedNames.Contains(project))
            .Concat(orphanedExternalProjects)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (analyzedProjects.Count > 0 || deletedProjects.Length > 0)
            await store.ReplaceProjectsAsync(analyzedProjects, deletedProjects, cancellationToken);

        var counts = await store.GetCountsAsync(cancellationToken);
        var indexedFiles = previousState.Projects.Sum(project => project.Files.Count)
            + changeSummary.Added
            - changeSummary.Removed;
        await WriteStateAsync(root, resolved, analyzedProjects, previousState, changeSummary.RemovedProjects, _lastProjectReferences, _lastPublicSurfaceFingerprints, _lastExternalAssembliesByOwningProject, cancellationToken);
        await ImportDeclaredScipProvidersAsync(root, cancellationToken);
        stopwatch.Stop();
        return new IndexSummary(
            Math.Max(0, indexedFiles),
            changeSummary.Added,
            changeSummary.Updated,
            changeSummary.Removed,
            changeSummary.Skipped,
            counts.Symbols,
            counts.Edges,
            stopwatch.Elapsed,
            analyzedProjects.Select(project => project.ProjectName).ToArray())
        {
            SkippedPropagationNotes = skippedPropagationNotes
        };
    }








    private static async Task<bool> IsSolutionInputChangedAsync(IndexStateFile previousState, string resolved, CancellationToken cancellationToken)
    {
        var currentHash = SqliteCodeMapStore.ComputeContentHash(resolved, await File.ReadAllTextAsync(resolved, cancellationToken));
        return !string.Equals(currentHash, previousState.ResolvedInputHash, StringComparison.Ordinal);
    }




















    private async Task<IndexSummary> ReconcileSolutionMembershipAsync(
        string root,
        string resolved,
        SqliteCodeMapStore store,
        IndexStateFile previousState,
        System.Diagnostics.Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var analyzedProjects = await AnalyzeAllAsync(root, resolved, csharpProjectNames: null, dirtyProjectNames: null, cancellationToken);
        var declaredNames = CSharpWorkspaceIndexer.GetDeclaredProjectNames(resolved);
        var removedProjects = previousState.Projects
            .Select(project => project.ProjectName)
            .Where(name => !name.StartsWith("web:", StringComparison.Ordinal) && !SqliteCodeMapStore.IsExternalProject(name) && !IsScipProject(name))
            .Where(name => !declaredNames.Contains(name))
            .ToHashSet(StringComparer.Ordinal);
        analyzedProjects = analyzedProjects
            .Where(project => project.ProjectName.StartsWith("web:", StringComparison.Ordinal)
                || SqliteCodeMapStore.IsExternalProject(project.ProjectName)
                || declaredNames.Contains(project.ProjectName))
            .ToArray();

        var orphanedExternalProjects = ComputeOrphanedExternalProjects(
            previousState, removedProjects, analyzedProjects, _lastExternalAssembliesByOwningProject);
        var deletedProjects = removedProjects
            .Concat(orphanedExternalProjects)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (analyzedProjects.Count > 0 || deletedProjects.Length > 0)
            await store.ReplaceProjectsAsync(analyzedProjects, deletedProjects, cancellationToken);

        var counts = await store.GetCountsAsync(cancellationToken);
        var previousFileCountByProject = previousState.Projects.ToDictionary(project => project.ProjectName, project => project.Files.Count, StringComparer.Ordinal);
        var added = analyzedProjects
            .Where(project => !previousFileCountByProject.ContainsKey(project.ProjectName))
            .Sum(project => project.Files.Count);
        var removedFileCount = removedProjects.Sum(name => previousFileCountByProject.GetValueOrDefault(name));
        var indexedFiles = analyzedProjects.Where(p => !SqliteCodeMapStore.IsExternalProject(p.ProjectName)).Sum(p => p.Files.Count);
        await WriteStateAsync(root, resolved, analyzedProjects, previousState, removedProjects, _lastProjectReferences, _lastPublicSurfaceFingerprints, _lastExternalAssembliesByOwningProject, cancellationToken);
        await ImportDeclaredScipProvidersAsync(root, cancellationToken);
        counts = await store.GetCountsAsync(cancellationToken);
        stopwatch.Stop();
        return new IndexSummary(
            Math.Max(0, indexedFiles),
            added,
            0,
            removedFileCount,
            0,
            counts.Symbols,
            counts.Edges,
            stopwatch.Elapsed,
            analyzedProjects.Select(project => project.ProjectName).ToArray());
    }






    public async Task<bool> IsUpToDateAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        var root = ResolveRoot(inputPath, out var resolved);
        if (!File.Exists(Path.Combine(root, ".codemap", "index.db")))
            return false;
        return await TryLoadUpToDateStateAsync(root, resolved, cancellationToken) is not null;
    }

    private static string Key(string project, string path) => project + "" + path.Replace('\\', '/');
















    private async Task<IReadOnlyList<AnalyzedProject>> ExpandAndAnalyzeUsingFingerprintsAsync(
        string root,
        string? resolved,
        HashSet<string> initiallyDirtyProjects,
        IReadOnlyDictionary<string, HashSet<string>> referencing,
        IndexStateFile previousState,
        CancellationToken cancellationToken,
        CSharpWorkspaceAnalysisResult? firstWaveSeed = null,
        ICollection<string>? skippedPropagationNotes = null)
    {
        var previousFingerprints = previousState.Projects
            .ToDictionary(project => project.ProjectName, project => project.PublicSurfaceFingerprint, StringComparer.Ordinal);

        var analyzed = new Dictionary<string, AnalyzedProject>(StringComparer.Ordinal);
        var accumulatedProjectReferences = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var accumulatedFingerprints = new Dictionary<string, string?>(StringComparer.Ordinal);
        var accumulatedExternalAssemblies = new Dictionary<string, IReadOnlyList<ExternalAssemblyReference>>(StringComparer.Ordinal);
        var processed = new HashSet<string>(StringComparer.Ordinal);
        var wave = new HashSet<string>(initiallyDirtyProjects, StringComparer.Ordinal);
        var isFirstWave = true;

        while (wave.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var waveNames = wave.ToArray();
            IReadOnlyList<AnalyzedProject> waveResult;
            if (isFirstWave && firstWaveSeed is not null)
                waveResult = await AnalyzeSeededFirstWaveAsync(root, firstWaveSeed, wave, cancellationToken);
            else
                waveResult = await AnalyzeAllAsync(root, resolved, csharpProjectNames: waveNames, dirtyProjectNames: wave, cancellationToken);
            isFirstWave = false;
            foreach (var project in waveResult)
                analyzed[project.ProjectName] = project;
            foreach (var (name, value) in _lastProjectReferences)
                accumulatedProjectReferences[name] = value;
            foreach (var (name, value) in _lastPublicSurfaceFingerprints)
                accumulatedFingerprints[name] = value;
            foreach (var (name, value) in _lastExternalAssembliesByOwningProject)
                accumulatedExternalAssemblies[name] = value;

            var nextWave = new HashSet<string>(StringComparer.Ordinal);
            foreach (var projectName in waveNames)
            {
                processed.Add(projectName);
                if (!referencing.TryGetValue(projectName, out var dependents))
                    continue;

                var previousFingerprint = previousFingerprints.GetValueOrDefault(projectName);
                var newFingerprintKnown = _lastPublicSurfaceFingerprints.TryGetValue(projectName, out var newFingerprint);





                var surfaceProvablyUnchanged = newFingerprintKnown
                    && newFingerprint is not null
                    && previousFingerprint is not null
                    && string.Equals(newFingerprint, previousFingerprint, StringComparison.Ordinal);
                if (surfaceProvablyUnchanged)
                {
                    skippedPropagationNotes?.Add(
                        $"{projectName} -> {string.Join(", ", dependents.OrderBy(dependent => dependent, StringComparer.Ordinal))} (public surface fingerprint unchanged)");
                    continue;
                }

                foreach (var dependent in dependents)
                    if (!processed.Contains(dependent))
                        nextWave.Add(dependent);
            }
            wave = nextWave;
        }

        _lastProjectReferences = accumulatedProjectReferences;
        _lastPublicSurfaceFingerprints = accumulatedFingerprints;
        _lastExternalAssembliesByOwningProject = accumulatedExternalAssemblies;
        return analyzed.Values.ToArray();
    }










    private async Task<IReadOnlyList<AnalyzedProject>> AnalyzeSeededFirstWaveAsync(
        string root,
        CSharpWorkspaceAnalysisResult seed,
        IReadOnlyCollection<string> dirtyProjectNames,
        CancellationToken cancellationToken)
    {
        _lastProjectReferences = seed.ProjectReferences;
        _lastPublicSurfaceFingerprints = seed.PublicSurfaceFingerprints;
        _lastExternalAssembliesByOwningProject = seed.ExternalAssembliesByOwningProject
            ?? new Dictionary<string, IReadOnlyList<ExternalAssemblyReference>>(StringComparer.Ordinal);
        var projects = new List<AnalyzedProject>(seed.Projects.Select(project => project.ToAnalyzedProject()));

        if (ShouldAnalyzeWeb(root, dirtyProjectNames))
        {
            var web = await _webWorkspaceIndexer.AnalyzeAsync(root, cancellationToken);
            if (web is not null)
                projects.Add(web);
        }

        return projects;
    }

    private async Task<IReadOnlyList<AnalyzedProject>> AnalyzeAllAsync(
        string root,
        string? resolved,
        IReadOnlyCollection<string>? csharpProjectNames,
        IReadOnlyCollection<string>? dirtyProjectNames,
        CancellationToken cancellationToken)
    {
        _lastProjectReferences = new Dictionary<string, string[]>(StringComparer.Ordinal);
        _lastPublicSurfaceFingerprints = new Dictionary<string, string?>(StringComparer.Ordinal);
        _lastExternalAssembliesByOwningProject = new Dictionary<string, IReadOnlyList<ExternalAssemblyReference>>(StringComparer.Ordinal);
        var projects = new List<AnalyzedProject>();
        if (resolved is not null)
        {
            var filter = csharpProjectNames is null ? null : csharpProjectNames.ToHashSet(StringComparer.Ordinal);
            var csharp = await _workspaceIndexer.AnalyzeWithReferencingGraphAsync(resolved, filter, cancellationToken);
            _lastProjectReferences = csharp.ProjectReferences;
            _lastPublicSurfaceFingerprints = csharp.PublicSurfaceFingerprints;
            _lastExternalAssembliesByOwningProject = csharp.ExternalAssembliesByOwningProject
                ?? new Dictionary<string, IReadOnlyList<ExternalAssemblyReference>>(StringComparer.Ordinal);
            projects.AddRange(csharp.Projects.Select(project => project.ToAnalyzedProject()));
        }

        if (ShouldAnalyzeWeb(root, dirtyProjectNames))
        {
            var web = await _webWorkspaceIndexer.AnalyzeAsync(root, cancellationToken);
            if (web is not null)
                projects.Add(web);
        }

        return projects;
    }

    private static string WebProjectName(string root) => "web:" + new DirectoryInfo(root).Name;

    private static bool ShouldAnalyzeWeb(string root, IReadOnlyCollection<string>? dirtyProjectNames)
    {
        if (dirtyProjectNames is null)
            return true;
        if (dirtyProjectNames.Count == 0)
            return false;
        return dirtyProjectNames.Contains(WebProjectName(root), StringComparer.Ordinal);
    }

    private static string ResolveRoot(string inputPath, out string? resolved)
    {
        var path = Path.GetFullPath(string.IsNullOrWhiteSpace(inputPath) ? Directory.GetCurrentDirectory() : inputPath);
        if (Directory.Exists(path))
        {
            resolved = TryResolveCSharpInput(path);
            return path;
        }
        if (File.Exists(path))
        {
            resolved = Path.GetExtension(path) is ".sln" or ".slnx" or ".csproj" ? path : null;
            return Path.GetDirectoryName(path)!;
        }
        throw new DirectoryNotFoundException(path);
    }

    private static string? TryResolveCSharpInput(string directory)
    {
        try { return CSharpWorkspaceIndexer.ResolveInput(directory); }
        catch (FileNotFoundException) { return null; }
    }

    private static bool IsScipProject(string projectName) =>
        projectName.StartsWith("scip:", StringComparison.Ordinal);

    private static async Task<IReadOnlyList<(string Name, string ArtifactPath, bool Exists)>> ReadDeclaredScipProvidersAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(root, ".codemap", "scip-providers.json");
        if (!File.Exists(manifestPath))
        {
            return [];
        }

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false));
        if (!document.RootElement.TryGetProperty("providers", out var providers))
        {
            return [];
        }

        var declared = new List<(string Name, string ArtifactPath, bool Exists)>();
        foreach (var provider in providers.EnumerateArray())
        {
            if (!provider.TryGetProperty("name", out var nameElement)
                || !provider.TryGetProperty("artifact", out var artifactElement))
            {
                continue;
            }

            var name = nameElement.GetString();
            var artifactRelative = artifactElement.GetString();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(artifactRelative))
            {
                continue;
            }

            var artifactPath = Path.IsPathRooted(artifactRelative)
                ? artifactRelative
                : Path.GetFullPath(Path.Combine(root, artifactRelative.Replace('/', Path.DirectorySeparatorChar)));
            declared.Add((name, artifactPath, File.Exists(artifactPath)));
        }

        return declared.OrderBy(item => item.Name, StringComparer.Ordinal).ToArray();
    }

    // 전체 인덱스 뒤에 다시 읽을 SCIP 산출물. 신선한 기록은 선언 여부와 상관없이 기록된 경로로
    // 다시 읽는다. 스테일 기록은 매니페스트에 선언되고 파일이 있을 때만 선언 경로로 읽고,
    // 그렇지 않으면 --force에서는 실패하고 일반 index에서는 이전처럼 다시 읽지 않는다.
    private static async Task<IReadOnlyList<(string Name, string ArtifactPath)>> ResolveScipImportsAsync(
        string root,
        bool throwOnStale,
        CancellationToken cancellationToken)
    {
        var declared = await ReadDeclaredScipProvidersAsync(root, cancellationToken);
        var imports = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var provider in await ListScipProvidersAsync(root, cancellationToken))
        {
            if (provider.Fresh)
            {
                imports[provider.Name] = provider.Artifact;
                continue;
            }

            var declaration = declared.FirstOrDefault(item => item.Exists && string.Equals(item.Name, provider.Name, StringComparison.Ordinal));
            if (declaration.Name is not null)
            {
                imports[provider.Name] = declaration.ArtifactPath;
            }
            else if (throwOnStale)
            {
                throw new InvalidOperationException($"SCIP provider '{provider.Project}' is stale. Re-import it before running index --force.");
            }
        }

        foreach (var declaration in declared.Where(item => item.Exists))
        {
            imports[declaration.Name] = declaration.ArtifactPath;
        }

        return imports
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => (item.Key, item.Value))
            .ToArray();
    }

    private static async Task ImportDeclaredScipProvidersAsync(string root, CancellationToken cancellationToken)
    {
        var importService = new ScipImportService();
        foreach (var declaration in (await ReadDeclaredScipProvidersAsync(root, cancellationToken).ConfigureAwait(false)).Where(item => item.Exists))
        {
            await importService.ImportAsync(root, declaration.ArtifactPath, new ScipImportOptions(declaration.Name), cancellationToken).ConfigureAwait(false);
        }
    }

    public static IncrementalCodeMapIndexer CreateDefault()
    {
        CodeMapEngineBootstrap.EnsureInitialized();
        return new IncrementalCodeMapIndexer(new CSharpWorkspaceIndexer(), new WebWorkspaceIndexer());
    }
}
