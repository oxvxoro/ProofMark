using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

public sealed class PathPolicyTests
{
    [Theory]
    [InlineData("docs/architecture.md", "docs/**", true)]
    [InlineData("docs/design/README.md", "docs/**", true)]
    [InlineData("README.md", "**/*.md", true)]
    [InlineData("src/Proof.Engine/Foo.cs", "docs/**", false)]
    [InlineData("src/Proof.Engine/Foo.cs", "**/*.md", false)]
    [InlineData("src/Proof.Engine/Foo.cs", "**/*.cs", true)]
    [InlineData("src/Proof.Engine/Foo.cs", "src/*/Foo.cs", true)]
    [InlineData("src/Proof.Engine/Foo.cs", "src/*/*.cs", true)]
    [InlineData("a/b/Foo.cs", "src/*/*.cs", false)]
    public void IsIgnored_GlobRules_MatchExpected(string path, string pattern, bool expected)
    {
        Assert.Equal(expected, PathPolicy.IsIgnored(path, [new PathRule(pattern, "ignore")]));
    }

    [Fact]
    public void IsIgnored_NonIgnoreEffect_DoesNotMatch()
    {
        Assert.False(PathPolicy.IsIgnored("docs/x.md", [new PathRule("docs/**", "required")]));
    }

    [Fact]
    public void IsIgnored_NormalizesSeparatorsAndLeadingSlash()
    {
        Assert.True(PathPolicy.IsIgnored("\\docs\\x.md", [new PathRule("docs/**", "ignore")]));
        Assert.True(PathPolicy.IsIgnored("/docs/x.md", [new PathRule("/docs/**", "ignore")]));
    }

    [Fact]
    public void ApplyPathPolicy_DocOnlyChange_BecomesEmptyChangeSet()
    {
        var snapshot = new SourceSnapshot(
            "root",
            "base",
            "head",
            false,
            "src",
            [
                new FileDelta(FileChangeKind.Modified, null, "docs/architecture.md", [], []),
                new FileDelta(FileChangeKind.Modified, null, "README.md", [], [])
            ],
            ChangeSetIsEmpty: false);

        var filtered = ProofOrchestrator.ApplyPathPolicy(
            snapshot,
            [new PathRule("docs/**", "ignore"), new PathRule("**/*.md", "ignore")]);

        Assert.True(filtered.ChangeSetIsEmpty);
        Assert.Empty(filtered.Files);
    }

    [Fact]
    public void ApplyPathPolicy_SourceFileChange_IsKept()
    {
        var delta = new FileDelta(FileChangeKind.Modified, null, "src/Proof.Engine/Foo.cs", [], []);
        var snapshot = new SourceSnapshot(
            "root",
            "base",
            "head",
            false,
            "src",
            [delta],
            ChangeSetIsEmpty: false);

        var filtered = ProofOrchestrator.ApplyPathPolicy(
            snapshot,
            [new PathRule("docs/**", "ignore"), new PathRule("**/*.md", "ignore")]);

        Assert.False(filtered.ChangeSetIsEmpty);
        Assert.Same(delta, Assert.Single(filtered.Files));
    }

    [Fact]
    public async Task PlanAsync_AppliesPathPolicyBeforeImpactAnalysis()
    {
        var provider = new CapturingImpactProvider();
        var orchestrator = new ProofOrchestrator(
            provider,
            new DeterministicProofPlanner(),
            new NoOpVerificationRunner(),
            new DeterministicProofEvaluator(),
            new ChangeCertificateBuilder());
        var snapshot = new SourceSnapshot(
            "root",
            "base",
            "head",
            true,
            "src",
            [
                new FileDelta(FileChangeKind.Modified, null, ".cursor/mcp.json", [], [new LineSpan(".cursor/mcp.json", 1, 2)]),
                new FileDelta(FileChangeKind.Modified, null, "src/App.cs", [], [new LineSpan("src/App.cs", 1, 2)])
            ],
            ChangeSetIsEmpty: false);

        var result = await orchestrator.PlanAsync(
            snapshot,
            "quick",
            CancellationToken.None,
            new ProofPolicy(PathRules: [new PathRule(".cursor/**", "ignore")]));

        Assert.Equal("src/App.cs", Assert.Single(provider.Request!.Spans).File);
        Assert.Equal("src/App.cs", Assert.Single(result.Snapshot.Files).NewPath);
    }

    private sealed class CapturingImpactProvider : IChangeImpactProvider
    {
        public ChangeRequest? Request { get; private set; }

        public Task<ChangeImpact> AnalyzeAsync(ChangeRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new ChangeImpact(
                request.BaseRevision,
                request.HeadRevision,
                request.Spans,
                [],
                [],
                [],
                [],
                "complete",
                false,
                SourceDigest: request.SourceDigest));
        }
    }

    private sealed class NoOpVerificationRunner : IVerificationRunner
    {
        public Task<VerificationRunResult> VerifyAsync(
            ProofPlan plan,
            VerificationPlan verificationPlan,
            string workspaceRoot,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
