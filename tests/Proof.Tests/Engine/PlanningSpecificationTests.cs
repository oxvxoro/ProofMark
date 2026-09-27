using Proof.Core;
using Proof.Engine.Planning.Specifications;

namespace Proof.Tests;

/// <summary>
/// PR-10이 추출한 선택적 명세의 단위 커버리지. 각 술어는
/// 순수 조건이므로 여기서 따로 검증한다.
/// </summary>
public sealed class PlanningSpecificationTests
{
    [Fact]
    public void ProductionSymbolSpecification_RejectsTestSymbols()
    {
        var spec = new ProductionSymbolSpecification();

        Assert.True(spec.Evaluate(Symbol("App", isTest: false)).IsSatisfied);
        Assert.False(spec.Evaluate(Symbol("App", isTest: true)).IsSatisfied);
    }

    [Fact]
    public void TestMappingRequiredSpecification_RespectsProjectScope()
    {
        var spec = new TestMappingRequiredSpecification(new ProofPolicy(TestMappingProjects: ["App"]));

        Assert.True(spec.Evaluate(Symbol("App", isTest: false)).IsSatisfied);
        Assert.False(spec.Evaluate(Symbol("Other", isTest: false)).IsSatisfied);
    }

    [Fact]
    public void TestMappingRequiredSpecification_IsSatisfiedByAllProjectsWhenUnscoped()
    {
        var spec = new TestMappingRequiredSpecification(new ProofPolicy());

        Assert.True(spec.Evaluate(Symbol("Anything", isTest: false)).IsSatisfied);
    }

    [Fact]
    public void ArchitectureEnabledSpecification_MatchesPolicyMode()
    {
        var spec = new ArchitectureEnabledSpecification();

        Assert.False(spec.Evaluate(new ProofPolicy(Architecture: ArchitecturePolicyMode.Off)).IsSatisfied);
        Assert.True(spec.Evaluate(new ProofPolicy(Architecture: ArchitecturePolicyMode.Advisory)).IsSatisfied);
        Assert.True(spec.Evaluate(new ProofPolicy(Architecture: ArchitecturePolicyMode.Required)).IsSatisfied);
    }

    [Fact]
    public void SemanticAppRelationSpecification_AcceptsOnlySemanticAppEdges()
    {
        var spec = new SemanticAppRelationSpecification(new ProofPolicy());

        Assert.True(spec.Evaluate(new ImpactRelation("r", "i", 1, "RoutesTo", "Semantic", 1.0)).IsSatisfied);
        Assert.False(spec.Evaluate(new ImpactRelation("r", "i", 1, "Calls", "Semantic", 1.0)).IsSatisfied);
        Assert.False(spec.Evaluate(new ImpactRelation("r", "i", 1, "RoutesTo", "Heuristic", 1.0)).IsSatisfied);
        Assert.False(spec.Evaluate(new ImpactRelation("r", "i", 1, "RoutesTo", "Semantic", 0.5)).IsSatisfied);
    }

    [Fact]
    public void CompleteCallerCoverageSpecification_FlagsTruncationAndMissingRecord()
    {
        var spec = new CompleteCallerCoverageSpecification();

        Assert.True(spec.Evaluate(null).IsSatisfied);
        Assert.True(spec.Evaluate(Completeness(callerTruncated: false)).IsSatisfied);

        var truncated = spec.Evaluate(Completeness(callerTruncated: true));
        Assert.False(truncated.IsSatisfied);
        Assert.Equal(ProofReasonCodes.CallerPotentiallyTruncated, truncated.ReasonCode);
    }

    private static ChangedSymbolRef Symbol(string project, bool isTest)
        => new("id-" + project, project, $"{project}/File.cs", "Display", 1, 2, IsPublic: true, IsTest: isTest);

    private static ImpactCompleteness Completeness(bool callerTruncated) => new(
        CoverageState.Complete,
        CoverageState.Complete,
        2,
        500,
        50,
        ImpactPotentiallyTruncated: false,
        CallerPotentiallyTruncated: callerTruncated,
        UnknownSpanCount: 0,
        HeuristicRelationCount: 0,
        MinimumConfidence: 1.0);
}
