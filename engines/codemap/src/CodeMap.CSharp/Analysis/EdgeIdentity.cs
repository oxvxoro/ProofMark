using CodeMap.Core.Models;

namespace CodeMap.CSharp.Analysis;

/// <summary>
/// 선언, 의미, 애플리케이션 보강 단계가 공유하는 안정적이고 위치 인식이
/// 있는 식별자. 이 값 객체를 Roslyn 객체와 분리해 두면 단계를 합칠 때
/// 중복 엣지가 생기지 않는다.
/// </summary>
internal readonly record struct EdgeIdentity(
    string SourceId,
    string TargetId,
    EdgeKind Kind,
    int? StartLine,
    int? StartColumn,
    int? EndLine,
    int? EndColumn)
{
    public static EdgeIdentity From(CodeEdge edge) => new(
        edge.SourceId,
        edge.TargetId,
        edge.Kind,
        edge.SourceLocation?.StartLine,
        edge.SourceLocation?.StartColumn,
        edge.SourceLocation?.EndLine,
        edge.SourceLocation?.EndColumn);

    public override string ToString() =>
        $"{SourceId}\u001f{TargetId}\u001f{Kind}\u001f{StartLine}\u001f{StartColumn}\u001f{EndLine}\u001f{EndColumn}";
}
