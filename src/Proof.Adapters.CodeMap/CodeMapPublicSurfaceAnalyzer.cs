using CodeMap.CSharp.Analysis;
using Proof.Core;

namespace Proof.Adapters.CodeMap;

internal static class CodeMapPublicSurfaceAnalyzer
{
    internal static IReadOnlyList<ApiCompatibilityFact> Analyze(
        IReadOnlyDictionary<string, string?> previousFingerprints,
        IReadOnlyDictionary<string, string?> currentFingerprints,
        IReadOnlyDictionary<string, IReadOnlyList<PublicSurfaceEntry>> previousEntries,
        IReadOnlyDictionary<string, IReadOnlyList<PublicSurfaceEntry>> currentEntries)
    {
        var projects = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var key in previousFingerprints.Keys)
        {
            projects.Add(key);
        }

        foreach (var key in currentFingerprints.Keys)
        {
            projects.Add(key);
        }

        foreach (var key in previousEntries.Keys)
        {
            projects.Add(key);
        }

        foreach (var key in currentEntries.Keys)
        {
            projects.Add(key);
        }

        var facts = new List<ApiCompatibilityFact>(projects.Count);
        foreach (var project in projects)
        {
            if (!PublicSurfaceSnapshotStore.IsRepositoryProject(project))
            {
                continue;
            }

            previousFingerprints.TryGetValue(project, out var previousFingerprint);
            currentFingerprints.TryGetValue(project, out var currentFingerprint);
            previousEntries.TryGetValue(project, out var previous);
            currentEntries.TryGetValue(project, out var current);
            facts.Add(new ApiCompatibilityFact(project, Classify(previousFingerprint, currentFingerprint, previous, current)));
        }

        return facts;
    }

    internal static ApiCompatibilityState Classify(
        string? previousFingerprint,
        string? currentFingerprint,
        IReadOnlyList<PublicSurfaceEntry>? previousEntries,
        IReadOnlyList<PublicSurfaceEntry>? currentEntries)
    {
        if (!string.IsNullOrWhiteSpace(previousFingerprint)
            && string.Equals(previousFingerprint, currentFingerprint, StringComparison.Ordinal))
        {
            return ApiCompatibilityState.Unchanged;
        }

        if (previousEntries is null || currentEntries is null)
        {
            return ApiCompatibilityState.Unknown;
        }

        return PublicSurfaceComparer.Compare(previousEntries, currentEntries).Kind switch
        {
            PublicSurfaceChangeKind.Unchanged => ApiCompatibilityState.Unchanged,
            PublicSurfaceChangeKind.Additive => ApiCompatibilityState.Additive,
            PublicSurfaceChangeKind.Breaking => ApiCompatibilityState.Breaking,
            PublicSurfaceChangeKind.Mixed => ApiCompatibilityState.Mixed,
            _ => ApiCompatibilityState.Unknown
        };
    }

    internal static EvidenceStatus ToEvidenceStatus(ApiCompatibilityState state)
        => state switch
        {
            ApiCompatibilityState.Unchanged or ApiCompatibilityState.Additive => EvidenceStatus.Pass,
            ApiCompatibilityState.Breaking or ApiCompatibilityState.Mixed => EvidenceStatus.Fail,
            _ => EvidenceStatus.Inconclusive
        };
}
