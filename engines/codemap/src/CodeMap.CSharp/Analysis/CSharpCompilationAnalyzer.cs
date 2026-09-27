using CodeMap.Core.Analysis;
using CodeMap.Core.Models;

namespace CodeMap.CSharp.Analysis;

/// <summary>
/// 단계형 C# 파이프라인의 명시적 진입점. 마이그레이션 동안 기존 분석기가
/// 구현으로 남아, 호출자가 이 경계로 옮겨도 ID와 의미 신뢰도는
/// 바뀌지 않는다.
/// </summary>
public sealed class CSharpCompilationAnalyzer(CSharpLanguageAnalyzer? analyzer = null)
{
    private readonly CSharpLanguageAnalyzer _analyzer = analyzer ?? new CSharpLanguageAnalyzer();

    public Task<AnalysisResult> AnalyzeAsync(AnalysisContext context, CancellationToken cancellationToken = default) =>
        _analyzer.AnalyzeAsync(context, cancellationToken);

    public async Task<CSharpAnalysisStages> AnalyzeStagesAsync(AnalysisContext context, CancellationToken cancellationToken = default)
    {
        var result = await AnalyzeAsync(context, cancellationToken).ConfigureAwait(false);
        var declarations = DeclarationCollector.Collect(result);
        return new CSharpAnalysisStages(
            result,
            declarations,
            AnonymousFunctionCollector.Collect(result),
            SemanticRelationCollector.Collect(result),
            PublicSurfaceFingerprinter.Compute(declarations),
            ExternalRootCollector.Collect(result));
    }
}

public sealed record CSharpAnalysisStages(
    AnalysisResult Result,
    IReadOnlyList<CodeNode> Declarations,
    IReadOnlyList<CodeNode> AnonymousFunctions,
    IReadOnlyList<CodeEdge> SemanticRelations,
    string PublicSurfaceFingerprint,
    IReadOnlySet<string> ExternalRoots);
