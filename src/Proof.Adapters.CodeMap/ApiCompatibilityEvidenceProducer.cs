using CodeMap.CSharp.Analysis;
using Proof.Core;

namespace Proof.Adapters.CodeMap;

public sealed class ApiCompatibilityEvidenceProducer : IApiCompatibilityEvidenceProducer
{
    private readonly IReadOnlyList<ApiCompatibilityFact> _facts;

    public ApiCompatibilityEvidenceProducer(IReadOnlyList<ApiCompatibilityFact>? facts = null)
    {
        _facts = facts ?? [];
    }

    public Task<IReadOnlyList<ProofEvidence>> AnalyzeAsync(
        ChangeRequest request,
        ProofPlan proofPlan,
        CancellationToken cancellationToken)
        => AnalyzeAsync(request, proofPlan, _facts, cancellationToken);

    public Task<IReadOnlyList<ProofEvidence>> AnalyzeAsync(
        ChangeRequest request,
        ProofPlan proofPlan,
        IReadOnlyList<ApiCompatibilityFact> facts,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var evidence = new List<ProofEvidence>();
        var index = 0;
        foreach (var obligation in proofPlan.Obligations.Where(item => item.Kind == ObligationKind.Compatibility))
        {
            index++;
            var project = obligation.Subject?.Project ?? obligation.SubjectId;
            var fact = facts.FirstOrDefault(item => string.Equals(item.Project, project, StringComparison.Ordinal));
            var status = fact is null
                ? EvidenceStatus.Inconclusive
                : CodeMapPublicSurfaceAnalyzer.ToEvidenceStatus(fact.State);
            evidence.Add(new ProofEvidence(
                $"API{index}",
                EvidenceKind.ApiCompatibility,
                obligation.SubjectId,
                status,
                new EvidenceProvenance(
                    "codemap",
                    SourceDigest: request.SourceDigest ?? proofPlan.SourceDigest,
                    CheckId: EvidenceCheckIds.ApiCompatibility),
                new EvidenceScope(
                    ScopeMode.Exact,
                    [obligation.Subject?.DisplayName ?? obligation.SubjectId],
                    project,
                    [
                        new EvidenceSubjectRef(
                            SubjectKind.ApiSurface,
                            obligation.SubjectId,
                            project,
                            obligation.Subject?.File,
                            obligation.Subject?.DisplayName)
                    ])));
        }

        return Task.FromResult<IReadOnlyList<ProofEvidence>>(evidence);
    }

    internal static EvidenceStatus ResolveStatus(
        string? previousFingerprint,
        string? currentFingerprint,
        IReadOnlyList<PublicSurfaceEntry>? previousEntries = null,
        IReadOnlyList<PublicSurfaceEntry>? currentEntries = null)
        => CodeMapPublicSurfaceAnalyzer.ToEvidenceStatus(
            CodeMapPublicSurfaceAnalyzer.Classify(previousFingerprint, currentFingerprint, previousEntries, currentEntries));

    internal static EvidenceStatus FromDiff(PublicSurfaceDiff diff)
        => diff.Kind switch
        {
            PublicSurfaceChangeKind.Unchanged or PublicSurfaceChangeKind.Additive => EvidenceStatus.Pass,
            PublicSurfaceChangeKind.Breaking or PublicSurfaceChangeKind.Mixed => EvidenceStatus.Fail,
            _ => EvidenceStatus.Inconclusive
        };
}
