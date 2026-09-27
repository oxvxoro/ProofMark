using Proof.Adapters.Distill;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

public sealed class DistillVerificationRunnerTests
{
    [Fact]
    public void ResolveStaticAnalysisProjectRefs_KeepsCloneProject_WhenDiagnosticsCarryEmptyProject()
    {
        var diagnostics = new[]
        {
            Distill.Core.Diagnostics.DistillDiagnostic.Create(
                "d1", Distill.Core.Diagnostics.DiagnosticKind.Analysis, Distill.Core.Diagnostics.DiagnosticSeverity.Warning,
                "sarif", "DEMO001", "demo", provenance: Distill.Core.Diagnostics.DiagnosticProvenance.Sarif)
        };

        var refs = DistillVerificationRunner.ResolveStaticAnalysisProjectRefs("Proof.Core", diagnostics);

        Assert.Equal("Proof.Core", Assert.Single(refs).Id);
    }

    [Fact]
    public void ResolveStaticAnalysisProjectRefs_AppendsDistinctDiagnosticProjects()
    {
        var diagnostics = new[]
        {
            Distill.Core.Diagnostics.DistillDiagnostic.Create(
                "d1", Distill.Core.Diagnostics.DiagnosticKind.Analysis, Distill.Core.Diagnostics.DiagnosticSeverity.Warning,
                "sarif", "DEMO001", "demo", project: "Other", provenance: Distill.Core.Diagnostics.DiagnosticProvenance.Sarif),
            Distill.Core.Diagnostics.DistillDiagnostic.Create(
                "d2", Distill.Core.Diagnostics.DiagnosticKind.Analysis, Distill.Core.Diagnostics.DiagnosticSeverity.Warning,
                "sarif", "DEMO001", "demo", project: "Proof.Core", provenance: Distill.Core.Diagnostics.DiagnosticProvenance.Sarif)
        };

        var refs = DistillVerificationRunner.ResolveStaticAnalysisProjectRefs("Proof.Core", diagnostics);

        Assert.Equal(new[] { "Proof.Core", "Other" }, refs.Select(reference => reference.Id).ToArray());
    }

    [Fact]
    public void ResolveConfigPath_PrefersExplicitConfiguration()
    {
        var path = DistillVerificationRunner.ResolveConfigPath("C:\\repo", "custom/distill.yml");
        Assert.Contains("custom", path.Replace('\\', '/'));
    }

    [Fact]
    public void VerificationPlanner_ReportsUncoveredCompatibility()
    {
        var plan = new ProofPlan([
            new ProofObligation("O1", "P001A", ObligationKind.Compatibility, "api", "s1", true, 4, ["r"])
        ]);
        var catalog = new[]
        {
            new EvidenceCapability("build", "build", "Proof.slnx", ScopeMode.RepositoryWide)
        };

        var verificationPlan = new VerificationPlanner().Plan(plan, catalog, "quick");
        Assert.Contains(verificationPlan.Uncovered, item => item.ObligationId == "O1");
    }

    [Fact]
    public void MergeProducerCapabilities_CoversCompatibility()
    {
        var plan = new ProofPlan([
            new ProofObligation("O1", "P001A", ObligationKind.Compatibility, "api", "s1", true, 4, ["r"])
        ]);
        var catalog = DistillVerificationRunner.MergeProducerCapabilities([
            new EvidenceCapability("build", "build", "Proof.slnx", ScopeMode.RepositoryWide)
        ], "full");
        var verificationPlan = new VerificationPlanner().Plan(plan, catalog, "full");
        Assert.DoesNotContain(verificationPlan.Uncovered, item => item.ObligationId == "O1");
        Assert.Contains(verificationPlan.Checks, item => item.CheckId == "api-compatibility");
    }

    [Fact]
    public void MergeProducerCapabilities_AlwaysCatalogsCompatibility_SelectionFollowsObligations()
    {
        // api-compatibility는 프로필과 관계없이 항상 카탈로그에 오른다.
        // 플래너는 P001A 호환성 의무가 있을 때만 그것을 선택한다.
        var catalog = DistillVerificationRunner.MergeProducerCapabilities([
            new EvidenceCapability("build", "build", "Proof.slnx", ScopeMode.RepositoryWide)
        ], "quick");
        Assert.Contains(catalog, item => item.CheckId == "api-compatibility");

        var plan = new ProofPlan([
            new ProofObligation("O2", "P003", ObligationKind.CrossProject, "build", "Consumer", true, 3, ["r"],
                new ProofSubject(SubjectKind.Project, "Consumer", "Consumer"))
        ]);
        var verificationPlan = new VerificationPlanner().Plan(plan, catalog, "quick");
        Assert.DoesNotContain(verificationPlan.Checks, item => item.CheckId == "api-compatibility");
    }

    [Fact]
    public void MergeProducerCapabilities_CatalogsTestMapping_AndPlannerSelectsForP005()
    {
        var catalog = DistillVerificationRunner.MergeProducerCapabilities([
            new EvidenceCapability("build", "build", "Proof.slnx", ScopeMode.RepositoryWide)
        ], "quick");
        Assert.Contains(catalog, item => item.CheckId == "test-mapping");

        var plan = new ProofPlan([
            new ProofObligation(
                "O1", "P005", ObligationKind.TestMapping, "mapping", "s1", true, 2, ["r"],
                new ProofSubject(SubjectKind.Symbol, "s1", "App"))
        ]);
        var verificationPlan = new VerificationPlanner().Plan(plan, catalog, "quick");
        Assert.DoesNotContain(verificationPlan.Uncovered, item => item.ObligationId == "O1");
        Assert.Contains(verificationPlan.Checks, item => item.CheckId == "test-mapping");
        Assert.DoesNotContain(verificationPlan.Checks, item => item.CheckId == "api-compatibility");
    }

    [Fact]
    public void VerificationPlanner_TestCapabilityCoversImpactedTest()
    {
        var plan = new ProofPlan([
            new ProofObligation(
                "O1",
                "P004",
                ObligationKind.Test,
                "impacted",
                "Tests.Foo.Bar",
                true,
                4,
                ["r"],
                new ProofSubject(SubjectKind.Test, "Tests.Foo.Bar", DisplayName: "Tests.Foo.Bar"))
        ]);
        var catalog = new[]
        {
            new EvidenceCapability("unit", "test", "tests/App.Tests/App.Tests.csproj", ScopeMode.Exact)
        };
        var verificationPlan = new VerificationPlanner().Plan(plan, catalog, "full");
        Assert.DoesNotContain(verificationPlan.Uncovered, item => item.ObligationId == "O1");
        Assert.Contains(verificationPlan.Checks, item => item.CheckId == "unit");

        var evidence = new ProofEvidence(
            "E1",
            EvidenceKind.TestCase,
            "Tests.Foo.Bar",
            EvidenceStatus.Pass,
            new EvidenceProvenance("distill", CheckId: "unit", SourceDigest: null),
            new EvidenceScope(
                ScopeMode.Exact,
                ["Tests.Foo.Bar"],
                "tests/App.Tests/App.Tests.csproj",
                [new EvidenceSubjectRef(SubjectKind.Test, "Tests.Foo.Bar", FullyQualifiedName: "Tests.Foo.Bar")]));
        var bound = new EvidenceBinder().Bind(plan, [evidence], verificationPlan);
        Assert.Equal(ProofVerdict.Proven, new DeterministicProofEvaluator().Evaluate(plan, bound).Verdict);
    }

    [Fact]
    public void VerificationPlanner_UncoveredRequiredCannotBeProven()
    {
        var plan = new ProofPlan([
            new ProofObligation("O1", "P001A", ObligationKind.Compatibility, "api", "s1", true, 4, ["r"])
        ]);
        var catalog = new[]
        {
            new EvidenceCapability("build", "build", "Proof.slnx", ScopeMode.RepositoryWide)
        };
        var verificationPlan = new VerificationPlanner().Plan(plan, catalog, "quick");
        Assert.Contains(verificationPlan.Uncovered, item => item.ObligationId == "O1");
        var evidence = new ProofEvidence(
            "E1",
            EvidenceKind.ApiCompatibility,
            "s1",
            EvidenceStatus.Pass,
            new EvidenceProvenance("codemap", CheckId: "api-compatibility"));
        var bound = new EvidenceBinder().Bind(plan, [evidence], verificationPlan);
        Assert.Empty(bound.Links);
        Assert.NotEqual(ProofVerdict.Proven, new DeterministicProofEvaluator().Evaluate(plan, bound).Verdict);
    }

    [Fact]
    public void BuildCatalog_UsesProfileUniverse()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-catalog-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "distill.yml"), """
                version: 1
                profiles:
                  quick:
                    checks: [build]
                  full:
                    checks: [build, unit]
                checks:
                  build:
                    kind: build
                    command: dotnet build App.slnx
                  unit:
                    kind: test
                    command: dotnet test tests/App.Tests/App.Tests.csproj
                """);

            var quick = DistillVerificationRunner.BuildCatalog(root, profile: "quick");
            var full = DistillVerificationRunner.BuildCatalog(root, profile: "full");
            Assert.Contains(quick, item => item.CheckId == "build");
            Assert.DoesNotContain(quick, item => item.CheckId == "unit");
            Assert.Contains(full, item => item.CheckId == "unit");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

public sealed class DistillCacheTests
{
    private static ProofPlan Plan(string digest = "src")
        => new([], SourceDigest: digest);

    private static VerificationPlan VerificationPlan(bool dirty = false)
        => new(
            [new PlannedVerificationCheck("build", "build", "dotnet build App.slnx")],
            [],
            "quick",
            SourceDirty: dirty);

    [Fact]
    public void IsCacheable_RequiresOptIn_CleanSnapshot_AndSourceDigest()
    {
        Environment.SetEnvironmentVariable("PROOF_CACHE", "1");
        try
        {
            var runner = new DistillVerificationRunner(cacheEnabled: false);
            Assert.True(runner.IsCacheable(Plan(), VerificationPlan()));
            Assert.True(runner.IsCacheable(Plan(), VerificationPlan(dirty: true)));
            Assert.False(runner.IsCacheable(Plan(digest: null!), VerificationPlan()));

            var optOutRunner = new DistillVerificationRunner(cacheEnabled: false);
            Environment.SetEnvironmentVariable("PROOF_CACHE", null);
            Assert.False(optOutRunner.IsCacheable(Plan(), VerificationPlan()));

            var optInRunner = new DistillVerificationRunner(cacheEnabled: true);
            Environment.SetEnvironmentVariable("PROOF_CACHE", null);
            Assert.True(optInRunner.IsCacheable(Plan(), VerificationPlan()));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROOF_CACHE", null);
        }
    }

    [Fact]
    public void ComputeCacheKey_IsSensitiveToDigest_Profile_AndCommands()
    {
        var baseKey = DistillVerificationRunner.ComputeCacheKey(Plan(), VerificationPlan());
        var digestKey = DistillVerificationRunner.ComputeCacheKey(Plan("other"), VerificationPlan());
        var profileKey = DistillVerificationRunner.ComputeCacheKey(Plan(), new VerificationPlan(
            [new PlannedVerificationCheck("build", "build", "dotnet build App.slnx")], [], "full"));
        var commandKey = DistillVerificationRunner.ComputeCacheKey(Plan(), new VerificationPlan(
            [new PlannedVerificationCheck("build", "build", "dotnet build Other.slnx")], [], "quick"));

        Assert.NotEqual(baseKey, digestKey);
        Assert.NotEqual(baseKey, profileKey);
        Assert.NotEqual(baseKey, commandKey);
        Assert.Equal(baseKey, DistillVerificationRunner.ComputeCacheKey(Plan(), VerificationPlan()));
    }

    [Fact]
    public void CacheRoundtrip_RejectsEvidenceWithMismatchedSourceDigest()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-cache-" + Guid.NewGuid().ToString("N"));
        try
        {
            var key = DistillVerificationRunner.ComputeCacheKey(Plan(), VerificationPlan());
            var evidence = new List<ProofEvidence>
            {
                new(
                    "E1",
                    EvidenceKind.Build,
                    "build",
                    EvidenceStatus.Pass,
                    new EvidenceProvenance("distill", SourceDigest: "stale-digest", CheckId: "build"),
                    null)
            };
            DistillVerificationRunner.WriteCache(root, key, evidence);

            Assert.Null(DistillVerificationRunner.TryReadCache(root, key, "src"));

            evidence[0] = evidence[0] with
            {
                Provenance = evidence[0].Provenance with { SourceDigest = "src" }
            };
            DistillVerificationRunner.WriteCache(root, key, evidence);
            var loaded = DistillVerificationRunner.TryReadCache(root, key, "src");

            Assert.NotNull(loaded);
            Assert.Equal("src", Assert.Single(loaded).Provenance.SourceDigest);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task VerifyAsync_CacheHit_ReturnsEmptyCoverageArtifactPaths()
    {
        Environment.SetEnvironmentVariable("PROOF_CACHE", "1");
        var root = Path.Combine(Path.GetTempPath(), "proof-cache-cov-" + Guid.NewGuid().ToString("N"));
        try
        {
            var key = DistillVerificationRunner.ComputeCacheKey(Plan(), VerificationPlan());
            var evidence = new List<ProofEvidence>
            {
                new(
                    "E1",
                    EvidenceKind.Build,
                    "build",
                    EvidenceStatus.Pass,
                    new EvidenceProvenance("distill", SourceDigest: "src", CheckId: "build"),
                    null)
            };
            DistillVerificationRunner.WriteCache(root, key, evidence);

            var runner = new DistillVerificationRunner(cacheEnabled: true);
            var result = await runner.VerifyAsync(Plan(), VerificationPlan(), root, CancellationToken.None);

            Assert.NotEmpty(result.Evidence.Evidence);
            Assert.Empty(result.CoverageArtifactPaths);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROOF_CACHE", null);
            Directory.Delete(root, recursive: true);
        }
    }
}
