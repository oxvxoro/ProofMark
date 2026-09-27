namespace CodeMap.Web;

/// <summary>지원하지 않는 스크립트 구문의 명시적 폴백 정책 경계.</summary>
public static class ScriptFallbackAnalyzer
{
    public static bool IsRequired(ScriptAnalysisMode mode) => RegexScriptFallbackAnalyzer.IsFallbackRequired(mode);
}
