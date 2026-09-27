namespace CodeMap.Engine.Application;

/// <summary>
/// 단명한 CLI와 장기 실행 MCP/LSP 호스트가 공유하는 신선도 계약.
/// 구현이 캐시 정책을 소유하고, 호출자는 보수적인 불리언만 관찰하며
/// 갱신 뒤에 저장소를 명시적으로 무효화한다.
/// </summary>
public interface IIndexFreshnessService
{
    Task<bool> IsUpToDateAsync(string root, CancellationToken cancellationToken = default);

    void Invalidate(string root);
}
