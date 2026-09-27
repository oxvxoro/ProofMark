using CodeMap.Core.Analysis;
using CodeMap.Core.Models;

namespace CodeMap.Web;

/// <summary>
/// 웹 분석 조합 경계. HTML/CSS/스크립트 파싱과 관계 해소는
/// WebLanguageAnalyzer와 동작이 호환된 채로 남고,
/// 어댑터는 단일 세션 진입점을 가진다.
/// </summary>
public sealed class WebAnalysisCoordinator(WebLanguageAnalyzer? analyzer = null)
{
    private readonly WebLanguageAnalyzer _analyzer = analyzer ?? new WebLanguageAnalyzer();

    public Task<AnalysisResult> AnalyzeAsync(AnalysisContext context, CancellationToken cancellationToken = default) =>
        _analyzer.AnalyzeAsync(context, cancellationToken);

    public async Task<WebAnalysisStages> AnalyzeStagesAsync(AnalysisContext context, CancellationToken cancellationToken = default)
    {
        var result = await AnalyzeAsync(context, cancellationToken).ConfigureAwait(false);
        var files = (context.SourceFiles?.Keys ?? Array.Empty<string>()).ToArray();
        return new WebAnalysisStages(
            result,
            files.Where(HtmlDocumentAnalyzer.Supports).ToArray(),
            files.Where(CssDocumentAnalyzer.Supports).ToArray(),
            files.Where(ScriptDocumentAnalyzer.Supports).ToArray(),
            WebRelationBuilder.SemanticEdges(result));
    }
}

public sealed record WebAnalysisStages(
    AnalysisResult Result,
    IReadOnlyList<string> HtmlFiles,
    IReadOnlyList<string> CssFiles,
    IReadOnlyList<string> ScriptFiles,
    IReadOnlyList<CodeEdge> Relations);
