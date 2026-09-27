using CodeMap.Storage;
using Proof.Core;

namespace Proof.Adapters.CodeMap;

// 영향 제공자가 이미 연 것과 같은 인덱싱된 그래프의 투영 뷰에서 CodeMap
// 아키텍처 검사를 실행한다. 두 번째 인덱스는 없다. 투영 로더는 SQL에서
// 휴리스틱 간선을 거른다. `ResolutionKind == Heuristic` 관계는 추측이며,
// invariant 9는 휴리스틱 관계가 필수 위반을 만드는 일을 절대 허용하지 않는다.
// 결정적 구문 간선(Razor/XAML Imports, confidence 0.85–0.9)은 유지한다.
// 이들을 버리면 실제 계층 위반이 숨는다. CodeMap.Storage의 기존 LoadAsync
// 의미는 그대로다.
internal static class ArchitectureCheckRunner
{
    public static async Task<(IReadOnlyList<ArchitectureViolationRef>? Violations, bool RulesPresent)> RunAsync(
        string workspaceRoot,
        string databasePath,
        CancellationToken cancellationToken)
    {
        var rulesPresent = File.Exists(Path.Combine(workspaceRoot, ".codemap", "architecture.json"));
        try
        {
            var rules = ArchitectureChecker.LoadRules(workspaceRoot);
            var projectReferences = ArchitectureChecker.LoadProjectReferences(workspaceRoot);
            var snapshot = await new CodeMapQueryStore(databasePath)
                .LoadArchitectureProjectionAsync(excludeHeuristic: true, cancellationToken)
                .ConfigureAwait(false);
            var violations = ArchitectureChecker.Check(snapshot, projectReferences, rules);
            return (violations.Select(item => ToRef(item, snapshot)).ToArray(), rulesPresent);
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidOperationException
                or UnauthorizedAccessException
                or System.Text.Json.JsonException
                or Microsoft.Data.Sqlite.SqliteException)
        {
            // "Not run"이 정직한 결과다. 플래너는 null 위반 목록을
            // unresolved 경로로 바꾸며, 결코 조용한 Pass로 바꾸지 않는다.
            // JsonException은 손으로 깨진 architecture.json /
            // state.json을 다룬다. 설정 파일 오류가 verify를 크래시시켜서는 안 된다.
            return (null, rulesPresent);
        }
    }

    private static ArchitectureViolationRef ToRef(ArchitectureViolation violation, CodeMapSnapshot snapshot)
    {
        var subjectId = violation.Source ?? string.Empty;
        var symbol = snapshot.FindById(subjectId);
        return new ArchitectureViolationRef(
            violation.Kind,
            violation.Message,
            subjectId,
            Project: symbol?.Project,
            File: symbol?.RelativePath,
            DisplayName: (symbol is null ? null : SymbolDisplayName.For(symbol)) ?? (subjectId.Length > 0 ? subjectId : null));
    }
}
