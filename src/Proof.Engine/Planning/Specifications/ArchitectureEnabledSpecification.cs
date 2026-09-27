using Proof.Core;

namespace Proof.Engine.Planning.Specifications;

/// <summary>아키텍처 분석이 켜진 정책과 맞는다(조언 또는 필수).</summary>
internal sealed class ArchitectureEnabledSpecification : IProofSpecification<ProofPolicy>
{
    public SpecificationResult Evaluate(ProofPolicy candidate)
        => candidate.Architecture == ArchitecturePolicyMode.Off
            ? SpecificationResult.NotSatisfied(explanation: "architecture policy is off")
            : SpecificationResult.Satisfied;
}
