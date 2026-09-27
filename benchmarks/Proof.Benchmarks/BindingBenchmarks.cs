using BenchmarkDotNet.Attributes;
using Proof.Core;
using Proof.Engine;

namespace Proof.Benchmarks;

/// <summary>
/// PR-06 EvidenceIndex가 겨냥하는 의무/증거 행렬에 걸친 바인더 비용.
/// 무관한 증거가 95%라서, 인덱스의 후보 가지치기가
/// 측정이 보상하는 부분이다.
/// </summary>
[MemoryDiagnoser]
public class BindingBenchmarks
{
    [Params(100, 500, 1000)]
    public int ObligationCount { get; set; }

    [Params(1000, 10000, 50000)]
    public int EvidenceCount { get; set; }

    private ProofPlan _plan = null!;
    private ProofEvidence[] _evidence = null!;

    [GlobalSetup]
    public void Setup()
    {
        var obligations = Enumerable.Range(0, ObligationCount)
            .Select(index => new ProofObligation(
                $"O{index}",
                "P004",
                ObligationKind.Test,
                $"claim{index}",
                $"sym{index}",
                true,
                4,
                ["r"],
                new ProofSubject(SubjectKind.Test, $"sym{index}", DisplayName: $"App.Tests.T{index}")))
            .ToArray();

        _evidence = new ProofEvidence[EvidenceCount];
        for (var index = 0; index < EvidenceCount; index++)
        {
            var subject = $"sym{index % ObligationCount}";
            // 5%만 관련된다. 나머지는 인덱스가 건너뛰어야 하는 잡음이다.
            _evidence[index] = index % 100 < 5
                ? new ProofEvidence(
                    $"E{index}",
                    EvidenceKind.TestCase,
                    subject,
                    EvidenceStatus.Pass,
                    new EvidenceProvenance("bench", CheckId: "unit", SourceDigest: "src"),
                    new EvidenceScope(
                        ScopeMode.Exact,
                        [subject],
                        null,
                        [new EvidenceSubjectRef(SubjectKind.Test, subject, FullyQualifiedName: $"App.Tests.T{index % ObligationCount}")]))
                : new ProofEvidence(
                    $"E{index}",
                    EvidenceKind.TestCase,
                    $"noise{index}",
                    EvidenceStatus.Pass,
                    new EvidenceProvenance("bench", CheckId: "unit", SourceDigest: "src"),
                    new EvidenceScope(ScopeMode.Contains, [$"noise{index}"]));
        }

        _plan = new ProofPlan(obligations, SourceDigest: "src");
    }

    [Benchmark]
    public int Bind() => new EvidenceBinder().Bind(_plan, _evidence).Links.Count;
}
