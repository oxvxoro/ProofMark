using CodeMap.Core.Models;

namespace CodeMap.Engine.Indexing;

public sealed record AnalysisWorkItem(
    string ProjectName,
    string ProjectPath,
    IReadOnlyList<AnalyzedSourceFile> Files,
    AnalyzerCapabilityLevel CapabilityLevel = AnalyzerCapabilityLevel.Semantic);

public delegate Task<AnalysisResult> AnalyzeWorkItemAsync(AnalysisWorkItem workItem, CancellationToken cancellationToken);

/// <summary>선택된 분석 작업을 조율하며 영속화에는 의존하지 않는다.</summary>
public sealed class AnalysisCoordinator(AnalyzeWorkItemAsync analyze)
{
    public async Task<IReadOnlyList<AnalyzedProject>> AnalyzeAsync(
        IEnumerable<AnalysisWorkItem> workItems,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItems);
        var results = new List<AnalyzedProject>();
        foreach (var workItem in workItems.OrderBy(item => item.ProjectName, StringComparer.Ordinal).ThenBy(item => item.ProjectPath, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await analyze(workItem, cancellationToken).ConfigureAwait(false);
            results.Add(new AnalyzedProject(workItem.ProjectName, workItem.ProjectPath, workItem.Files, result, workItem.CapabilityLevel));
        }
        return results;
    }
}
