using System.Text.Json;
using Proof.Cli;
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

public sealed class ProofConfigWaveBTests
{
    [Fact]
    public void Load_ParsesTestMaps_Paths_And_CiCodes()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-waveb-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "proof.yml"), """
                version: 2
                verification:
                  distillProfile: quick
                policy:
                  testMaps:
                    - symbol: "OrderService.Cancel"
                      tests:
                        - Proof.Tests.FooTests.Bar
                  paths:
                    - match: "docs/**"
                      effect: ignore
                  ci:
                    failOnUncertainCodes:
                      - REQUIRED_EVIDENCE_MISSING
                """);

            var config = ProofConfig.Load(root);
            var policy = config.ToPolicy();

            var map = Assert.Single(policy.TestMaps!);
            Assert.Equal("OrderService.Cancel", map.Symbol);
            Assert.Equal(["Proof.Tests.FooTests.Bar"], map.Tests);
            var rule = Assert.Single(policy.PathRules!);
            Assert.Equal("docs/**", rule.Match);
            Assert.Equal("ignore", rule.Effect);
            Assert.Equal(["REQUIRED_EVIDENCE_MISSING"], policy.FailOnUncertainCodes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_RejectsUnknownCiKey()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-waveb-bad-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "proof.yml"), """
                version: 2
                verification:
                  distillProfile: quick
                policy:
                  ci:
                    failOnUnknown: true
                """);

            Assert.Throws<global::Proof.Core.ProofConfigException>(() => ProofConfig.Load(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ToPolicy_RejectsUnsupportedPathEffect()
    {
        var config = new ProofConfig();
        config.Policy.Paths.Add(new PathRuleSection { Match = "docs/**", Effect = "required" });

        var exception = Assert.Throws<global::Proof.Core.ProofConfigException>(() => config.ToPolicy());
        Assert.Contains("required", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("manual-review", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_ParsesTestMappingProjects_AndRoundTripsToPolicy()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-waveb-scope-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "proof.yml"), """
                version: 2
                verification:
                  distillProfile: quick
                policy:
                  testMapping: required
                  testMappingProjects:
                    - Proof.Engine
                    - proof.core
                """);

            var config = ProofConfig.Load(root);
            Assert.Equal(["Proof.Engine", "proof.core"], config.Policy.TestMappingProjects);

            var policy = config.ToPolicy();
            Assert.Equal(["Proof.Engine", "proof.core"], policy.TestMappingProjects);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ToPolicy_OmittedTestMappingProjects_MapsToNull()
    {
        var policy = new ProofConfig().ToPolicy();
        Assert.Null(policy.TestMappingProjects);
    }

    [Fact]
    public void Load_RejectsUnknownTestMappingProjectKey()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-waveb-scope-bad-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "proof.yml"), """
                version: 2
                verification:
                  distillProfile: quick
                policy:
                  testMappingProjectz:
                    - Proof.Engine
                """);

            Assert.Throws<global::Proof.Core.ProofConfigException>(() => ProofConfig.Load(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ToPolicy_RejectsEmptyTestMappingProjectEntry()
    {
        var config = new ProofConfig();
        config.Policy.TestMappingProjects = ["Proof.Engine", "  "];

        var exception = Assert.Throws<global::Proof.Core.ProofConfigException>(() => config.Validate());
        Assert.Contains("testMappingProjects", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class ShouldFailCiTests
{
    private static ChangeCertificate Certificate(
        ProofVerdict verdict,
        ProofEvaluation? evaluation = null,
        IReadOnlyList<AnalysisConstraint>? constraints = null)
        => new(
            3,
            "base",
            "head",
            verdict,
            new ChangeImpact("base", "head", [], [], [], [], [], "complete", false),
            new ProofPlan([], Constraints: constraints),
            new VerificationEvidenceSet([], []),
            evaluation ?? new ProofEvaluation(verdict, [], ReasonCode: null));

    private static ProofPolicy Policy(params string[] codes)
        => new(FailOnUncertainCodes: codes);

    [Fact]
    public void Uncertain_ListedReasonCode_Fails()
    {
        var certificate = Certificate(
            ProofVerdict.Uncertain,
            new ProofEvaluation(ProofVerdict.Uncertain, [], ReasonCode: ProofReasonCodes.SourceFreshnessDrift));
        Assert.True(VerifyCommand.ShouldFailCi(certificate, Policy(ProofReasonCodes.SourceFreshnessDrift)));
    }

    [Fact]
    public void Uncertain_UnlistedReasonCode_StaysAdvisory()
    {
        var certificate = Certificate(
            ProofVerdict.Uncertain,
            new ProofEvaluation(ProofVerdict.Uncertain, [], ReasonCode: ProofReasonCodes.SourceFreshnessDrift));
        Assert.False(VerifyCommand.ShouldFailCi(certificate, Policy(ProofReasonCodes.RequiredEvidenceMissing)));
    }

    [Fact]
    public void Uncertain_ListedBlockingConstraint_Fails()
    {
        var constraint = new AnalysisConstraint("c1", ProofReasonCodes.RequiredEvidenceMissing, "blocking", "msg");
        var certificate = Certificate(ProofVerdict.Uncertain, constraints: [constraint]);
        Assert.True(VerifyCommand.ShouldFailCi(certificate, Policy(ProofReasonCodes.RequiredEvidenceMissing)));
    }

    [Fact]
    public void Uncertain_ListedObligationReason_Fails()
    {
        var obligation = new ProofObligation("O1", "P004", ObligationKind.Test, "claim", "t", true, 4, [ProofReasonCodes.RequiredEvidenceMissing]);
        var certificate = Certificate(
            ProofVerdict.Uncertain,
            new ProofEvaluation(ProofVerdict.Uncertain, [new EvaluatedObligation(obligation, ObligationStatus.Unresolved, [])]));
        Assert.True(VerifyCommand.ShouldFailCi(certificate, Policy(ProofReasonCodes.RequiredEvidenceMissing)));
    }

    [Fact]
    public void NoCodesConfigured_NeverFails()
    {
        var certificate = Certificate(
            ProofVerdict.Uncertain,
            new ProofEvaluation(ProofVerdict.Uncertain, [], ReasonCode: ProofReasonCodes.SourceFreshnessDrift));
        Assert.False(VerifyCommand.ShouldFailCi(certificate, new ProofPolicy()));
    }

    [Fact]
    public void ProvenVerdict_NeverFails()
    {
        var certificate = Certificate(ProofVerdict.Proven);
        Assert.False(VerifyCommand.ShouldFailCi(certificate, Policy(ProofReasonCodes.RequiredEvidenceMissing)));
    }

    [Fact]
    public void Uncertain_FallbackRequiredEvidenceMissing_IsAdvisoryWhenNotListed()
    {
        var obligation = new ProofObligation(
            "O1",
            "P005",
            ObligationKind.TestMapping,
            "mapping",
            "s1",
            true,
            2,
            ["no mapped test relation found"]);
        var certificate = Certificate(
            ProofVerdict.Uncertain,
            new ProofEvaluation(ProofVerdict.Uncertain, [new EvaluatedObligation(obligation, ObligationStatus.Unresolved, [])]));

        Assert.False(VerifyCommand.ShouldFailCi(
            certificate,
            Policy(
                ProofReasonCodes.ChangeDeletionAnalysisUnavailable,
                ProofReasonCodes.ChangeCaptureUntrackedFailed,
                ProofReasonCodes.SourceFreshnessDrift)));
    }

    [Fact]
    public void Uncertain_RequiredP005ProseReason_ListedFallbackCode_Fails()
    {
        var obligation = new ProofObligation(
            "O1",
            "P005",
            ObligationKind.TestMapping,
            "mapping",
            "s1",
            true,
            2,
            ["no mapped test relation found"]);
        var certificate = Certificate(
            ProofVerdict.Uncertain,
            new ProofEvaluation(ProofVerdict.Uncertain, [new EvaluatedObligation(obligation, ObligationStatus.Unresolved, [])]));

        Assert.True(VerifyCommand.ShouldFailCi(certificate, Policy(ProofReasonCodes.RequiredEvidenceMissing)));
    }

    [Fact]
    public void Uncertain_AdvisoryP005_NeverFailsByItself()
    {
        var advisory = new ProofObligation(
            "O1",
            "P005",
            ObligationKind.TestMapping,
            "mapping",
            "s1",
            false,
            2,
            ["no mapped test relation found"]);
        var certificate = Certificate(
            ProofVerdict.Uncertain,
            new ProofEvaluation(ProofVerdict.Uncertain, [new EvaluatedObligation(advisory, ObligationStatus.Unresolved, [])]));

        Assert.False(VerifyCommand.ShouldFailCi(certificate, Policy(ProofReasonCodes.RequiredEvidenceMissing)));
    }

    [Fact]
    public void Uncertain_RequiredNonMappingProseReason_StaysAdvisoryEvenWhenFallbackCodeListed()
    {
        // REQUIRED_EVIDENCE_MISSING 폴백은 test-mapping
        // 의무(P005)로 범위가 한정된다. 계획되었으나 묶이지 않은 필수 P004가
        // 이유로 가진 것이 플래너 산문뿐이면 변경 전과 같은 권고 처리를 유지하며
        // 폴백 코드로 병합을 절대 막지 않는다.
        var obligation = new ProofObligation(
            "O1",
            "P004",
            ObligationKind.Test,
            "test",
            "t1",
            true,
            4,
            ["CodeMap found impacted test symbol"]);
        var certificate = Certificate(
            ProofVerdict.Uncertain,
            new ProofEvaluation(ProofVerdict.Uncertain, [new EvaluatedObligation(obligation, ObligationStatus.Unresolved, [])]));

        Assert.False(VerifyCommand.ShouldFailCi(certificate, Policy(ProofReasonCodes.RequiredEvidenceMissing)));
    }

    [Fact]
    public void Uncertain_RequiredCallerContractProseReason_StaysAdvisoryEvenWhenFallbackCodeListed()
    {
        // P002도 범위가 같다. supporting 전용 바인딩(이유가 산문뿐)은
        // 권고로 남고, 의무에 *나열된* 실제 코드는 여전히 차단한다
        // (Uncertain_ListedObligationReason_Fails 참조).
        var obligation = new ProofObligation(
            "O1",
            "P002",
            ObligationKind.CallerContract,
            "caller",
            "c1",
            true,
            3,
            ["CodeMap detected caller relation"]);
        var certificate = Certificate(
            ProofVerdict.Uncertain,
            new ProofEvaluation(ProofVerdict.Uncertain, [new EvaluatedObligation(obligation, ObligationStatus.Unresolved, [])]));

        Assert.False(VerifyCommand.ShouldFailCi(certificate, Policy(ProofReasonCodes.RequiredEvidenceMissing)));
    }

    [Fact]
    public void Uncertain_AdvisoryP005_DoesNotHideRequiredDrift()
    {
        var advisory = new ProofObligation(
            "O1",
            "P005",
            ObligationKind.TestMapping,
            "mapping",
            "s1",
            false,
            2,
            ["no mapped test relation found"]);
        var certificate = Certificate(
            ProofVerdict.Uncertain,
            new ProofEvaluation(ProofVerdict.Uncertain, [new EvaluatedObligation(advisory, ObligationStatus.Unresolved, [])], ReasonCode: ProofReasonCodes.SourceFreshnessDrift));

        Assert.True(VerifyCommand.ShouldFailCi(certificate, Policy(ProofReasonCodes.SourceFreshnessDrift)));
    }
}

public sealed class CertificateFileNameTests
{
    [Fact]
    public void IsCertificateFile_ExcludesSummarySidecar()
    {
        Assert.True(VerifyCommand.IsCertificateFile("20260101120000-proof-abc.json"));
        Assert.False(VerifyCommand.IsCertificateFile("20260101120000-proof-abc.summary.json"));
        var names = new[]
        {
            "20260101120000-proof-abc.json",
            "20260101120000-proof-abc.summary.json"
        };
        var latestIfUnfiltered = names.OrderBy(value => value, StringComparer.Ordinal).Last();
        Assert.Equal("20260101120000-proof-abc.summary.json", latestIfUnfiltered);
        var latestCertificate = names.Where(VerifyCommand.IsCertificateFile).OrderBy(value => value, StringComparer.Ordinal).Last();
        Assert.Equal("20260101120000-proof-abc.json", latestCertificate);
    }
}

public sealed class CertificateSummaryTests
{
    [Fact]
    public void ToSummary_ContainsCountsAndDoesNotAffectStatementDigest()
    {
        var obligation = new ProofObligation("O1", "P004", ObligationKind.Test, "claim", "t", true, 4, [ProofReasonCodes.RequiredEvidenceMissing]);
        var proven = new ProofObligation("O2", "P006", ObligationKind.Uncertainty, "claim", "u", true, 1, ["r"]);
        var impact = new ChangeImpact("base", "head", [], [], [], [], [], "complete", false);
        var plan = new ProofPlan([obligation, proven]);
        var bound = new VerificationEvidenceSet([], []);
        var evaluation = new ProofEvaluation(
            ProofVerdict.Uncertain,
            [new EvaluatedObligation(obligation, ObligationStatus.Unresolved, []), new EvaluatedObligation(proven, ObligationStatus.Proven, [])],
            ReasonCode: ProofReasonCodes.RequiredEvidenceMissing);
        var certificate = new ChangeCertificateBuilder().Build(impact, plan, bound, evaluation);
        var before = certificate.StatementDigest;

        var summary = ChangeCertificateBuilder.ToSummary(certificate with { StatementDigest = "changed-by-sidecar" });

        Assert.Equal(1, summary.ProvenObligations);
        Assert.Equal(1, summary.UnresolvedObligations);
        Assert.Equal("P004", Assert.Single(summary.Unresolved).RuleId);
        Assert.Equal(ProofReasonCodes.RequiredEvidenceMissing, Assert.Single(summary.Unresolved).ReasonCode);
        Assert.Equal("t", Assert.Single(summary.Unresolved).SubjectId);
        Assert.Equal(before, certificate.StatementDigest);
    }

    [Fact]
    public void SummaryCommand_LoadsSummarySidecarWithoutRebuilding()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-summary-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, ".proof", "certificates");
        Directory.CreateDirectory(directory);
        var summaryPath = Path.Combine(directory, "20260101000000-proof-abc.summary.json");
        var summary = new CertificateSummary(
            ProofVerdict.Uncertain,
            ProofReasonCodes.ImpactLocationUnknown,
            "source",
            "certificate",
            "statement",
            1,
            1,
            [new SummaryUnresolvedObligation("P006", "claim", ObligationStatus.Unresolved, ProofReasonCodes.ImpactLocationUnknown, "subject", "file.cs")],
            []);
        File.WriteAllText(summaryPath, JsonSerializer.Serialize(summary, ProofJson.WireOptions));
        try
        {
            var previous = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(root);
            try
            {
                var code = SummaryCommand.Execute(summaryPath, "json");
                Assert.Equal(0, code);
            }
            finally
            {
                Directory.SetCurrentDirectory(previous);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
