using CodeMap.Core.Models;

namespace CodeMap.Engine.Indexing;

public sealed record IndexWorkflowResult(
    DirtyProjectPlan Plan,
    IReadOnlyList<AnalyzedProject> AnalyzedProjects);

/// <summary>
/// 명시적 인덱싱 파이프라인: 계획, 분석, 커밋 순서. 기존
/// IncrementalCodeMapIndexer는 공개 IndexAsync/UpdateAsync 계약을
/// 바꾸지 않고 이 워크플로를 점진적으로 채택할 수 있다.
/// </summary>
public sealed class IndexingWorkflow(
    DirtyProjectPlanner planner,
    AnalysisCoordinator analysis,
    IndexCommitter committer)
{
    public async Task<IndexWorkflowResult> RunAsync(
        RepositoryChangeSet changes,
        ProjectDependencyGraph graph,
        IReadOnlyDictionary<string, string?> previousFingerprints,
        IReadOnlyDictionary<string, string?> newFingerprints,
        IEnumerable<AnalysisWorkItem> workItems,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(workItems);
        var plan = planner.Plan(changes, graph, previousFingerprints, newFingerprints);
        var selected = workItems.Where(item => plan.ProjectsToAnalyze.Contains(item.ProjectName)).ToArray();
        var analyzed = await analysis.AnalyzeAsync(selected, cancellationToken).ConfigureAwait(false);
        await committer.CommitAsync(
            new IndexCommitRequest(analyzed, plan.RemovedProjects, metadata ?? new Dictionary<string, string>(StringComparer.Ordinal)),
            cancellationToken).ConfigureAwait(false);
        return new IndexWorkflowResult(plan, analyzed);
    }
}
