using CodeMap.Storage;
using CodeMap.Core.Models;

namespace CodeMap.Engine.Indexing;

/// <summary>
/// Engine이 바라보는 인덱싱 파사드. 저장소 네임스페이스의 인덱서는
/// 오케스트레이션 이전이 끝날 때까지 소스 호환을 위해 계속 쓸 수 있다.
/// </summary>
public sealed class CodeMapIndexer(IncrementalCodeMapIndexer? implementation = null)
{
    private readonly IncrementalCodeMapIndexer _implementation = implementation ?? IncrementalCodeMapIndexer.CreateDefault();

    public Task<IndexSummary> IndexAsync(string inputPath, bool force = false, CancellationToken cancellationToken = default) =>
        _implementation.IndexAsync(inputPath, force, cancellationToken);

    public Task<IndexSummary> UpdateAsync(string inputPath, CancellationToken cancellationToken = default) =>
        _implementation.UpdateAsync(inputPath, cancellationToken);

    public Task<bool> IsUpToDateAsync(string inputPath, CancellationToken cancellationToken = default) =>
        _implementation.IsUpToDateAsync(inputPath, cancellationToken);
}
