using CodeMap.Core.Models;

namespace CodeMap.Core.Contracts;

public sealed record IndexCommitBatch(
    IReadOnlyCollection<AnalyzedProject> Projects,
    IReadOnlyCollection<string> RemovedProjects,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>인덱싱 오케스트레이션이 사용하는 영속화 포트.</summary>
public interface ICodeMapIndexWriter
{
    Task CommitAsync(IndexCommitBatch batch, CancellationToken cancellationToken = default);
}
