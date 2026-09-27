namespace Proof.Core;

public sealed record ImpactAnalysisSettings(
    int BaseDepth = 2,
    int PublicDepth = 3,
    int MaxResults = 500,
    int CallerPageSize = 50,
    int CallerMaxResults = 50,
    double MinConfidence = 0.75,
    string Profile = "code");
