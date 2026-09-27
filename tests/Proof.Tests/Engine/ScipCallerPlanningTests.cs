using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

/// <summary>
/// CodeMap 호출자 조회는 scip: 프로젝트의 semantic References도 호출자로 준다.
/// P002는 테스트 호출자만 만든다. scip: 프로젝트를 테스트로 분류하는 규칙은 아직 없으므로
/// 비테스트 References 호출자는 P002를 만들지 않는다.
/// </summary>
public sealed class ScipCallerPlanningTests
{
    private static ProofPlan Plan(bool isTest)
    {
        var changed = new ChangedSymbolRef(
            "sym://M:App.Api.Load", "App", "App/Api.cs", "App.Api.Load()", 1, 5, IsPublic: true, IsTest: false);
        var impact = new ChangeImpact(
            "base", "head",
            [new LineSpan("App/Api.cs", 1, 5)],
            [changed],
            [],
            ["App"], ["scip://web/app.load"],
            "complete", false,
            Callers:
            [
                new CallerRelation(changed.Id, "scip://web/app.load", "scip:web", "web/app.ts", "Semantic", 1.0, isTest)
            ],
            SourceDigest: "src");
        return new DeterministicProofPlanner().Plan(impact, new ProofPolicy
        {
            PublicApiCompatibilityRequired = false,
            InternalConsumerCompatibilityRequired = false,
            TestMappingRequired = false,
        });
    }

    [Fact]
    public void NonTestScipReferenceCaller_DoesNotCreateP002()
        => Assert.DoesNotContain(Plan(isTest: false).Obligations, item => item.RuleId == "P002");

    [Fact]
    public void TestScipReferenceCaller_CreatesP002()
        => Assert.Contains(Plan(isTest: true).Obligations, item => item.RuleId == "P002" && item.SubjectId == "scip://web/app.load");
}
