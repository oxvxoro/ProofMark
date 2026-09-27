using Proof.Core;

namespace Proof.Engine.Planning.Specifications;

/// <summary>
/// 프로젝트가 필수 테스트 매핑 범위에 있는 변경 심볼과 맞는다.
/// <c>TestMappingScope.IsRequired</c>를 따라 규칙이 술어 하나를 공유한다.
/// </summary>
internal sealed class TestMappingRequiredSpecification(ProofPolicy policy)
    : IProofSpecification<ChangedSymbolRef>
{
    private readonly ProofPolicy _policy = policy;

    public SpecificationResult Evaluate(ChangedSymbolRef candidate)
        => TestMappingScope.IsRequired(candidate.Project, _policy)
            ? SpecificationResult.Satisfied
            : SpecificationResult.NotSatisfied(
                explanation: $"project '{candidate.Project}' is outside the required test-mapping scope");
}
