namespace Proof.Engine.Planning.Specifications;

/// <summary>
/// 재사용 가능한 정책 술어의 결과. reason/explanation은 조언이다.
/// 실패한 술어가 의무나 제약을 바꾸는지는 규칙이 결정한다.
/// </summary>
internal readonly record struct SpecificationResult(
    bool IsSatisfied,
    string? ReasonCode = null,
    string? Explanation = null)
{
    public static readonly SpecificationResult Satisfied = new(true);

    public static SpecificationResult NotSatisfied(string? reasonCode = null, string? explanation = null)
        => new(false, reasonCode, explanation);
}

/// <summary>
/// 계획 데이터 위의 재사용 가능한 조건 하나. 명세는
/// "이 후보가 정책을 만족하는가?"에만 답한다. 의무,
/// 제약, id를 결코 만들지 않고, IO, 그래프 순회, 해시도 결코 하지 않는다.
/// </summary>
internal interface IProofSpecification<in T>
{
    SpecificationResult Evaluate(T candidate);
}
