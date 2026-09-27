using Proof.Core;

namespace Proof.Engine;

public enum AnalysisDegradationCode
{
    FileWideFallback,
    UntrackedCaptureFailed,
    ContentHashCaptureFailed,
    StructuredDeltaFailed
}

public sealed record AnalysisDegradation(
    AnalysisDegradationCode Code,
    string? Subject = null);

public sealed record AnalysisQuality(IReadOnlyList<AnalysisDegradation> Degradations)
{
    public bool Has(AnalysisDegradationCode code)
        => Degradations.Any(item => item.Code == code);

    internal static AnalysisQuality From(ChangeRequest request, ChangeImpact impact)
    {
        var degradations = new List<AnalysisDegradation>(4);
        if (request.UsedFileWideFallback || impact.UsedFileWideFallback)
        {
            degradations.Add(new AnalysisDegradation(AnalysisDegradationCode.FileWideFallback));
        }

        if (request.UntrackedCaptureFailed || impact.UntrackedCaptureFailed)
        {
            degradations.Add(new AnalysisDegradation(AnalysisDegradationCode.UntrackedCaptureFailed));
        }

        if (request.ContentHashCaptureFailed || impact.ContentHashCaptureFailed)
        {
            degradations.Add(new AnalysisDegradation(AnalysisDegradationCode.ContentHashCaptureFailed));
        }

        if (request.StructuredDeltaFailed || impact.StructuredDeltaFailed)
        {
            degradations.Add(new AnalysisDegradation(AnalysisDegradationCode.StructuredDeltaFailed));
        }

        return new AnalysisQuality(degradations);
    }
}
