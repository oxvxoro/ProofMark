using Proof.Core;

namespace Proof.Adapters.CodeMap;

public sealed record CodeMapAnalysisOptions(
    string WorkspaceRoot,
    string? SolutionPath = null,
    ImpactAnalysisSettings? Impact = null,
    bool IndexBaseRevision = false,
    bool RunArchitectureCheck = false);
