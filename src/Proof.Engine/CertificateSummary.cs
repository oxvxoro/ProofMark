using Proof.Core;

namespace Proof.Engine;

public sealed record CertificateSummary(
    ProofVerdict Verdict,
    string? ReasonCode,
    string? SourceDigest,
    string? CertificateDigest,
    string? StatementDigest,
    int ProvenObligations,
    int UnresolvedObligations,
    IReadOnlyList<SummaryUnresolvedObligation> Unresolved,
    IReadOnlyList<SummaryConstraint> BlockingConstraints);

// CLI/CI가 엔진을 다시 돌리지 않고 아직 열린 심볼(예: P005)을
// 정확히 나열하도록 SubjectId/File을 담는다. 요약의 나머지와 같이
// 파생 출력이며 statement 다이제스트의 일부가 결코 아니다.
public sealed record SummaryUnresolvedObligation(
    string RuleId,
    string Claim,
    ObligationStatus Status,
    string? ReasonCode,
    string? SubjectId = null,
    string? File = null);

public sealed record SummaryConstraint(string Code, string Message, string? Subject);
