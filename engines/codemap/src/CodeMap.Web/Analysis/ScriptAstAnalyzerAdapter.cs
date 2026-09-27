using CodeMap.Core.Models;

namespace CodeMap.Web;

/// <summary>AST 스크립트 분석의 어댑터 경계. 마이그레이션 동안에는 기존 분석기가 기준 구현으로 남는다.</summary>
public static class ScriptAstAnalyzerAdapter
{
    public static IReadOnlyList<CodeEdge> ExtractRelations(AnalysisResult result) => result.Edges.ToArray();
}
