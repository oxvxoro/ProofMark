namespace Distill.Core.Evidence;

public sealed record BuildEvidence(
    IReadOnlyList<Diagnostics.DistillDiagnostic> Diagnostics,
    bool Succeeded,
    string? BinlogPath,
    IReadOnlyList<Diagnostics.DistillDiagnostic> Warnings,
    IReadOnlyList<string>? BuiltProjects = null,
    IReadOnlyList<string>? BuiltAssemblies = null);
