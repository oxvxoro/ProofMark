namespace Proof.Core;

/// <summary>메트릭 사이드카가 쓰는 실행당 카운터.</summary>
public sealed record VerificationMetricCounts(
    int ChangedSymbols,
    int ImpactedSymbols,
    int CallerRelations,
    int Obligations,
    int Evidence,
    int Links);

/// <summary>
/// 한 검증 실행의 단계 시간과 개수. 진단
/// 사이드카일 뿐이다. 서명된 statement 페이로드나
/// 인증서 다이제스트의 일부가 결코 아니다.
/// </summary>
public sealed record VerificationMetrics(
    double? SnapshotMs,
    double ImpactMs,
    double PlanningMs,
    double VerificationMs,
    double BindingMs,
    double EvaluationMs,
    double CertificateMs,
    VerificationMetricCounts Counts);

/// <summary>오케스트레이터가 실행당 메트릭을 기록하는 싱크.</summary>
public interface IProofMetricsSink
{
    void Record(VerificationMetrics metrics);
}

/// <summary>가장 최근에 기록된 실행을 담는 메모리 수집기.</summary>
public sealed class ProofMetricsCollector : IProofMetricsSink
{
    public VerificationMetrics? Last { get; private set; }

    public void Record(VerificationMetrics metrics) => Last = metrics;
}
