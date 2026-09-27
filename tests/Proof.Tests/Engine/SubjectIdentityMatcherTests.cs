using Proof.Core;

namespace Proof.Tests;

public sealed class SubjectIdentityMatcherTests
{
    [Fact]
    public void ExactId_Matches()
    {
        var kind = SubjectIdentityMatcher.Match(
            new SubjectIdentity("App.Service.Run"),
            new SubjectIdentity("App.Service.Run"));
        Assert.Equal(IdentityMatchKind.Exact, kind);
    }

    [Fact]
    public void SignatureTrim_IsExact()
    {
        Assert.Equal("App.Service.Run", SubjectIdentityMatcher.TrimSignature("App.Service.Run()"));
        Assert.Equal(
            IdentityMatchKind.Exact,
            SubjectIdentityMatcher.Match(
                new SubjectIdentity("id", DisplayName: "App.Service.Run()"),
                new SubjectIdentity("other", FullyQualifiedName: "App.Service.Run")));
    }

    [Fact]
    public void DottedSuffix_MatchesBothDirections()
    {
        Assert.True(SubjectIdentityMatcher.SymbolMatches(
            "Proof.Engine.DeterministicProofPlanner.Plan",
            null,
            "DeterministicProofPlanner.Plan"));
        Assert.True(SubjectIdentityMatcher.SymbolMatches(
            "DeterministicProofPlanner.Plan",
            null,
            "Proof.Engine.DeterministicProofPlanner.Plan"));
    }

    [Fact]
    public void BareShortName_IsNotExactSuffix()
    {
        Assert.False(SubjectIdentityMatcher.SymbolMatches("Plan", null, "DeterministicProofPlanner.Plan"));
        Assert.Equal(
            IdentityMatchKind.Heuristic,
            SubjectIdentityMatcher.Match(
                new SubjectIdentity("Run"),
                new SubjectIdentity(null, FullyQualifiedName: "App.Service.Run")));
    }

    [Fact]
    public void ProjectMismatch_IsNone()
    {
        Assert.Equal(
            IdentityMatchKind.None,
            SubjectIdentityMatcher.Match(
                new SubjectIdentity("App.Service.Run", Project: "App"),
                new SubjectIdentity("App.Service.Run", Project: "Other")));
    }

    [Fact]
    public void TargetFrameworkMismatch_IsNone()
    {
        Assert.Equal(
            IdentityMatchKind.None,
            SubjectIdentityMatcher.Match(
                new SubjectIdentity("App.Service.Run", TargetFramework: "net10.0"),
                new SubjectIdentity("App.Service.Run", TargetFramework: "net8.0")));
    }

    [Fact]
    public void SubjectId_ExactComparisonStaysCaseSensitive()
    {
        var kind = SubjectIdentityMatcher.Match(
            new SubjectIdentity("TokenLeft"),
            new SubjectIdentity("tokenleft", FullyQualifiedName: "Other.Symbol"));
        Assert.NotEqual(IdentityMatchKind.Exact, kind);
    }
}
