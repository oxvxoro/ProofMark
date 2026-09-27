using Proof.Core;

namespace Proof.Engine.Planning.Specifications;

/// <summary>제품 코드인 변경 심볼과 맞는다(테스트가 아니다).</summary>
internal sealed class ProductionSymbolSpecification : IProofSpecification<ChangedSymbolRef>
{
    public SpecificationResult Evaluate(ChangedSymbolRef candidate)
        => candidate.IsTest
            ? SpecificationResult.NotSatisfied(explanation: "changed symbol is a test symbol")
            : SpecificationResult.Satisfied;
}
