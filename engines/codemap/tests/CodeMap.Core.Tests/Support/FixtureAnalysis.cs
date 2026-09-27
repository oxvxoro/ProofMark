using CodeMap.CSharp;

namespace CodeMap.Core.Tests;

/// <summary>
/// 저장소 안 복원된 픽스처의 워크스페이스 분석을 프로세스당 한 번만 한다.
/// 테스트는 결과를 읽기만 하고 픽스처 파일을 바꾸지 않는다.
/// </summary>
internal static class FixtureAnalysis
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static IReadOnlyList<CSharpProjectAnalysis>? _aspNet;
    private static IReadOnlyList<CSharpProjectAnalysis>? _wpf;

    internal static async Task<IReadOnlyList<CSharpProjectAnalysis>> AspNetAsync()
        => await GetAsync("AspNetFixture", () => _aspNet, value => _aspNet = value);

    internal static async Task<IReadOnlyList<CSharpProjectAnalysis>> WpfAsync()
        => await GetAsync("WpfFixture", () => _wpf, value => _wpf = value);

    private static async Task<IReadOnlyList<CSharpProjectAnalysis>> GetAsync(
        string fixtureName,
        Func<IReadOnlyList<CSharpProjectAnalysis>?> read,
        Action<IReadOnlyList<CSharpProjectAnalysis>> write)
    {
        var cached = read();
        if (cached is not null)
            return cached;

        await Gate.WaitAsync();
        try
        {
            cached = read();
            if (cached is not null)
                return cached;

            var path = FixtureRestore.EnsureRestored(fixtureName);
            var projects = await new CSharpWorkspaceIndexer().AnalyzeAsync(path, null, CancellationToken.None);
            write(projects);
            return projects;
        }
        finally
        {
            Gate.Release();
        }
    }
}
