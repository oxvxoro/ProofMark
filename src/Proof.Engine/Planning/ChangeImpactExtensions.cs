using Proof.Core;

namespace Proof.Engine.Planning;

internal static class ChangeImpactExtensions
{
    public static bool ChangeSetIsEmptyExplicit(this ChangeImpact impact)
        => impact.Spans.Count == 0
           && impact.ChangedSymbols.Count == 0
           && impact.FileDeltas is not { Count: > 0 };
}
