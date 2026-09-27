using Proof.Core;

namespace Proof.Engine.Determinism;

/// <summary>
/// 인증서에 보이는 순서의 유일한 소유자. 의무와 제약의
/// 순서는 statement 페이로드의 일부이므로, 단계마다 다시 유도되어
/// 어긋나는 대신 한곳에 있다.
/// </summary>
internal static class ProofOrdering
{
    public static IOrderedEnumerable<ProofObligation> Obligations(IEnumerable<ProofObligation> source)
        => source
            .OrderByDescending(item => item.Required)
            .ThenBy(item => item.RuleId, StringComparer.Ordinal)
            .ThenBy(item => item.Subject?.Kind.ToString() ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(item => item.SubjectId, StringComparer.Ordinal)
            .ThenBy(item => item.Claim, StringComparer.Ordinal);

    public static IOrderedEnumerable<AnalysisConstraint> Constraints(IEnumerable<AnalysisConstraint> source)
        => source
            .OrderBy(item => item.Code, StringComparer.Ordinal)
            .ThenBy(item => item.Subject ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(item => item.Message, StringComparer.Ordinal);
}
