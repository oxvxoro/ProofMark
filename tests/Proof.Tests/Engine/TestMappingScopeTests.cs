using Proof.Core;

namespace Proof.Tests;

public sealed class TestMappingScopeTests
{
    private static ProofPolicy Policy(
        bool required = true,
        IReadOnlyList<string>? projects = null)
        => new(TestMappingRequired: required, TestMappingProjects: projects);

    [Fact]
    public void NotRequired_AlwaysFalse()
    {
        Assert.False(TestMappingScope.IsRequired("Proof.Engine", Policy(required: false, projects: ["Proof.Engine"])));
        Assert.False(TestMappingScope.IsRequired("Proof.Engine", Policy(required: false)));
    }

    [Fact]
    public void NullOrEmptyList_IsGlobal()
    {
        Assert.True(TestMappingScope.IsRequired("Proof.Engine", Policy()));
        Assert.True(TestMappingScope.IsRequired("CodeMap.Engine", Policy(projects: [])));
        Assert.True(TestMappingScope.IsRequired(null, Policy()));
    }

    [Fact]
    public void ExactMatch_IgnoringCase()
    {
        Assert.True(TestMappingScope.IsRequired("Proof.Engine", Policy(projects: ["Proof.Engine"])));
        Assert.True(TestMappingScope.IsRequired("proof.engine", Policy(projects: ["Proof.Engine"])));
        Assert.True(TestMappingScope.IsRequired("Proof.Engine", Policy(projects: ["proof.engine"])));
    }

    [Fact]
    public void PartialName_NeverMatches()
    {
        Assert.False(TestMappingScope.IsRequired("Proof.Engine", Policy(projects: ["Proof"])));
        Assert.False(TestMappingScope.IsRequired("Proof.Engine", Policy(projects: ["Proof.Engine.Tests"])));
        Assert.False(TestMappingScope.IsRequired("CodeMap.Engine", Policy(projects: ["Proof.Engine"])));
    }

    [Fact]
    public void EmptyOrUnknownProject_WithNonEmptyList_IsOutOfScope()
    {
        Assert.False(TestMappingScope.IsRequired(null, Policy(projects: ["Proof.Engine"])));
        Assert.False(TestMappingScope.IsRequired("", Policy(projects: ["Proof.Engine"])));
        Assert.False(TestMappingScope.IsRequired("  ", Policy(projects: ["Proof.Engine"])));
    }

    [Fact]
    public void ProofReasonCodes_IsKnown_SeparatesCodesFromProse()
    {
        Assert.True(ProofReasonCodes.IsKnown(ProofReasonCodes.RequiredEvidenceMissing));
        Assert.True(ProofReasonCodes.IsKnown(ProofReasonCodes.SourceFreshnessDrift));
        Assert.False(ProofReasonCodes.IsKnown("no mapped test relation found"));
        Assert.False(ProofReasonCodes.IsKnown(""));
        Assert.False(ProofReasonCodes.IsKnown(null));
    }
}
