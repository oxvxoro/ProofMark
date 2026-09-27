using Proof.Core;

namespace Proof.Engine.Planning.Specifications;

/// <summary>
/// 완전한(절단되지 않은) 호출자 커버리지를 가진 영향과 맞는다. 완전성
/// 기록이 없으면 완전한 것으로 본다. 이전 null 검사와 같다.
/// </summary>
internal sealed class CompleteCallerCoverageSpecification
    : IProofSpecification<ImpactCompleteness?>
{
    public SpecificationResult Evaluate(ImpactCompleteness? candidate)
        => candidate?.CallerPotentiallyTruncated == true
            ? SpecificationResult.NotSatisfied(
                ProofReasonCodes.CallerPotentiallyTruncated,
                "caller collection reached a result limit")
            : SpecificationResult.Satisfied;
}
