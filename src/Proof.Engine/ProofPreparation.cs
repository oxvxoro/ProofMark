using Proof.Core;

namespace Proof.Engine;

internal sealed record AnalyzedChange(
    ChangeRequest Request,
    ChangeImpact Impact,
    IReadOnlyList<ApiCompatibilityFact> ApiCompatibilityFacts);

internal sealed record PreparedProof(
    AnalyzedChange Analysis,
    ProofPlan ProofPlan,
    VerificationPlan? VerificationPlan);

internal static class ProofPreparation
{
    internal static ChangeImpact NormalizeImpact(ChangeRequest request, ChangeImpact impact)
    {
        var quality = AnalysisQuality.From(request, impact);
        return impact with
        {
            SourceDigest = request.SourceDigest ?? impact.SourceDigest,
            UsedFileWideFallback = quality.Has(AnalysisDegradationCode.FileWideFallback),
            UntrackedCaptureFailed = quality.Has(AnalysisDegradationCode.UntrackedCaptureFailed),
            ContentHashCaptureFailed = quality.Has(AnalysisDegradationCode.ContentHashCaptureFailed),
            StructuredDeltaFailed = quality.Has(AnalysisDegradationCode.StructuredDeltaFailed),
            FileDeltas = request.FileDeltas ?? impact.FileDeltas
        };
    }
}
