using Distill.Core.Config;
using Distill.Core.Planning;
using Proof.Adapters.Distill;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

public sealed class CoberturaCoverageParserTests
{
    private const string Fixture = """
        <?xml version="1.0" encoding="utf-8"?>
        <coverage line-rate="0.5">
          <packages>
            <package name="App">
              <classes>
                <class name="App.OrderService" filename="OrderService.cs">
                  <methods>
                    <method name="Cancel" signature="()">
                      <lines><line number="1" hits="0" /><line number="2" hits="0" /></lines>
                    </method>
                    <method name="Refund" signature="()">
                      <lines><line number="3" hits="1" /></lines>
                    </method>
                  </methods>
                </class>
                <class name="App.Billing.BillingService" filename="BillingService.cs">
                  <methods>
                    <method name="Pay" signature="()">
                      <lines><line number="1" hits="2" /><line number="2" hits="0" /></lines>
                    </method>
                  </methods>
                </class>
              </classes>
            </package>
          </packages>
        </coverage>
        """;

    [Fact]
    public void Parse_ReturnsOnlyExecutedMethods_OrderedAndDistinct()
    {
        var parsed = CoberturaCoverageParser.Parse(Fixture);

        Assert.Equal(["App.Billing.BillingService.Pay", "App.OrderService.Refund"], parsed);
    }

    [Fact]
    public void Parse_InvalidXml_ReturnsEmpty()
    {
        Assert.Empty(CoberturaCoverageParser.Parse("<coverage>"));
    }

    [Fact]
    public void Parse_Empty_ReturnsEmpty()
    {
        Assert.Empty(CoberturaCoverageParser.Parse(string.Empty));
    }
}

public sealed class RuntimeCoverageEvidenceProducerTests
{
    private static ChangeRequest Request(string workspaceRoot)
        => new(workspaceRoot, "base", "head", [], SourceDigest: "src");

    private static ChangeImpact Impact() => new(
        "base",
        "head",
        [new LineSpan("Services/OrderService.cs", 1, 2)],
        [new ChangedSymbolRef("sym-cancel", "App", "Services/OrderService.cs", "App.OrderService.Cancel", 1, 2, true, false)],
        [],
        ["App"],
        [],
        "complete",
        false,
        SourceDigest: "src");

    private static ProofPlan Plan(params ProofObligation[] obligations)
        => new(obligations, SourceDigest: "src");

    private static ProofObligation P005(string id, string subjectId, string displayName)
        => new(id, "P005", ObligationKind.TestMapping, "mapping", subjectId, true, 2, ["r"],
            new ProofSubject(SubjectKind.Symbol, subjectId, "App", DisplayName: displayName));

    private static string WriteCoverage(string workspaceRoot)
    {
        const string xml = """
            <coverage>
              <packages><package name="App"><classes>
                <class name="App.OrderService" filename="OrderService.cs">
                  <methods>
                    <method name="Cancel"><lines><line number="1" hits="1" /></lines></method>
                    <method name="Refund"><lines><line number="1" hits="0" /></lines></method>
                  </methods>
                </class>
              </classes></package></packages>
            </coverage>
            """;
        var relative = "coverage/coverage.cobertura.xml";
        var full = Path.Combine(workspaceRoot, "coverage", "coverage.cobertura.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, xml);
        return relative;
    }

    [Fact]
    public async Task Producer_DisplayNameSuffixMatch_ClosesP005()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-cov-" + Guid.NewGuid().ToString("N"));
        try
        {
            var relative = WriteCoverage(root);
            var plan = Plan(P005("O1", "codemap-symbol-id", "OrderService.Cancel"));
            var producer = new RuntimeCoverageEvidenceProducer();

            var evidence = await producer.AnalyzeAsync(Request(root), Impact(), plan, [relative], CancellationToken.None);

            var item = Assert.Single(evidence);
            Assert.Equal(EvidenceKind.RuntimeCoverage, item.Kind);
            Assert.Equal(EvidenceStatus.Pass, item.Status);
            Assert.Equal("runtime-coverage", item.Provenance.CheckId);
            Assert.Equal("src", item.Provenance.SourceDigest);
            Assert.Equal(relative, item.Provenance.ArtifactPointer);
            Assert.NotNull(item.Provenance.Sha256);
            Assert.Contains(item.Scope!.SubjectRefs!, reference => reference.Id == "codemap-symbol-id");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Producer_UnmatchedSymbol_EmitsNothing()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-cov-" + Guid.NewGuid().ToString("N"));
        try
        {
            var relative = WriteCoverage(root);
            var plan = Plan(P005("O1", "s2", "OrderService.Ship"));
            var producer = new RuntimeCoverageEvidenceProducer();

            var evidence = await producer.AnalyzeAsync(Request(root), Impact(), plan, [relative], CancellationToken.None);

            Assert.Empty(evidence);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Producer_NoArtifacts_EmitsNothing()
    {
        var producer = new RuntimeCoverageEvidenceProducer();
        var evidence = await producer.AnalyzeAsync(
            Request("root"), Impact(), Plan(P005("O1", "s1", "OrderService.Cancel")), [], CancellationToken.None);
        Assert.Empty(evidence);
    }

    [Fact]
    public async Task Producer_TwoObligationsMatchedByOneHit_EachGetsOwnEvidence()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-cov-" + Guid.NewGuid().ToString("N"));
        try
        {
            var relative = WriteCoverage(root);
            var plan = Plan(
                P005("O1", "s1", "OrderService.Cancel"),
                P005("O2", "s2", "App.OrderService.Cancel"));
            var producer = new RuntimeCoverageEvidenceProducer();

            var evidence = await producer.AnalyzeAsync(Request(root), Impact(), plan, [relative], CancellationToken.None);

            Assert.Equal(2, evidence.Count);
            Assert.Contains(evidence, item => item.Scope!.SubjectRefs!.Any(reference => reference.Id == "s1"));
            Assert.Contains(evidence, item => item.Scope!.SubjectRefs!.Any(reference => reference.Id == "s2"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

public sealed class RuntimeCoveragePlannerTests
{
    [Fact]
    public void P005_KeepsBothTestMappingAndRuntimeCoverageInPlan()
    {
        var plan = new ProofPlan([
            new ProofObligation(
                "O1", "P005", ObligationKind.TestMapping, "mapping", "s1", true, 2, ["r"],
                new ProofSubject(SubjectKind.Symbol, "s1", "App", DisplayName: "OrderService.Cancel"))
        ]);
        var catalog = DistillVerificationRunner.MergeProducerCapabilities([], "quick");

        var verificationPlan = new VerificationPlanner().Plan(plan, catalog, "quick");

        Assert.Contains(verificationPlan.Checks, item => item.CheckId == "test-mapping");
        Assert.Contains(verificationPlan.Checks, item => item.CheckId == "runtime-coverage");
        Assert.DoesNotContain(verificationPlan.Uncovered, item => item.ObligationId == "O1");
    }

    [Fact]
    public void AdvisoryP005_SelectsNeitherRuntimeCoverageNorTestHost()
    {
        var plan = new ProofPlan([
            new ProofObligation(
                "O1", "P005", ObligationKind.TestMapping, "mapping", "s1", false, 2, ["r"],
                new ProofSubject(SubjectKind.Symbol, "s1", "App", DisplayName: "OrderService.Cancel"))
        ]);
        var catalog = DistillVerificationRunner.MergeProducerCapabilities(
            [new EvidenceCapability("proof-tests", "test", "App.Tests.csproj", ScopeMode.RepositoryWide)],
            "quick");

        var verificationPlan = new VerificationPlanner().Plan(plan, catalog, "quick");

        Assert.DoesNotContain(verificationPlan.Checks, item => item.CheckId == "test-mapping");
        Assert.DoesNotContain(verificationPlan.Checks, item => item.CheckId == "runtime-coverage");
        Assert.DoesNotContain(verificationPlan.Checks, item => item.CheckId == "proof-tests");
    }

    [Fact]
    public void RequiredP005_WithQuickLikeTestCheck_SelectsRuntimeCoverageAndKeepsTestHost()
    {
        var plan = new ProofPlan([
            new ProofObligation(
                "O1", "P005", ObligationKind.TestMapping, "mapping", "s1", true, 2, ["r"],
                new ProofSubject(SubjectKind.Symbol, "s1", "App", DisplayName: "OrderService.Cancel"))
        ]);
        var catalog = DistillVerificationRunner.MergeProducerCapabilities(
            [new EvidenceCapability("proof-tests", "test", "App.Tests.csproj", ScopeMode.RepositoryWide)],
            "quick");

        var verificationPlan = new VerificationPlanner().Plan(plan, catalog, "quick");

        Assert.Contains(verificationPlan.Checks, item => item.CheckId == "runtime-coverage");
        Assert.Contains(verificationPlan.Checks, item => item.CheckId == "proof-tests");
        Assert.DoesNotContain(verificationPlan.Uncovered, item => item.ObligationId == "O1");
    }

    [Fact]
    public void NoP005_DoesNotSelectProducerChecks()
    {
        var plan = new ProofPlan([
            new ProofObligation(
                "O1", "P003", ObligationKind.CrossProject, "build", "App", true, 3, ["r"],
                new ProofSubject(SubjectKind.Project, "App", "App"))
        ]);
        var catalog = DistillVerificationRunner.MergeProducerCapabilities(
            [new EvidenceCapability("build", "build", "App.slnx", ScopeMode.RepositoryWide)],
            "quick");

        var verificationPlan = new VerificationPlanner().Plan(plan, catalog, "quick");

        Assert.DoesNotContain(verificationPlan.Checks, item => item.CheckId == "test-mapping");
        Assert.DoesNotContain(verificationPlan.Checks, item => item.CheckId == "runtime-coverage");
        Assert.Contains(verificationPlan.Checks, item => item.CheckId == "build::App");
    }
}

public sealed class RuntimeCoverageRewriteTests
{
    [Fact]
    public void RewriteCommand_NoCoverageDirectory_LeavesCommandUnchanged()
    {
        var rewritten = DistillVerificationRunner.RewriteCommand(
            "dotnet test tests/Foo.Tests/Foo.Tests.csproj --no-build",
            "C:/repo",
            null,
            null);

        Assert.DoesNotContain("--collect", rewritten);
        Assert.DoesNotContain("--results-directory", rewritten);
        Assert.Contains("--no-build", rewritten);
    }

    [Fact]
    public void RewriteCommand_CoverageDirectory_AddsCollectAndResults()
    {
        var rewritten = DistillVerificationRunner.RewriteCommand(
            "dotnet test tests/Foo.Tests/Foo.Tests.csproj",
            "C:/repo",
            null,
            null,
            "C:/repo/.distill/runs/run1/coverage");

        Assert.Contains("--collect", rewritten);
        Assert.Contains("--results-directory:", rewritten);
        Assert.Contains(".distill/runs/run1/coverage", rewritten);
    }

    [Fact]
    public void RewriteCommand_CoverageIgnoredForNonTestVerb()
    {
        var rewritten = DistillVerificationRunner.RewriteCommand(
            "dotnet build App.slnx",
            "C:/repo",
            null,
            null,
            "C:/repo/.distill/runs/run1/coverage");

        Assert.DoesNotContain("--collect", rewritten);
        Assert.DoesNotContain("--results-directory", rewritten);
    }

    [Fact]
    public void RewriteCommand_CollectToken_RoundTripsAsSingleArgument()
    {
        var rewritten = DistillVerificationRunner.RewriteCommand(
            "dotnet test tests/Foo.Tests/Foo.Tests.csproj",
            "C:/repo",
            null,
            null,
            "C:/repo/coverage");
        var parsed = DotnetCommandParser.Parse(rewritten);

        Assert.Contains("--collect:XPlat Code Coverage", parsed.Arguments);
        Assert.Contains(parsed.Arguments, argument => argument.StartsWith("--results-directory:", StringComparison.Ordinal));
    }

    [Fact]
    public void ResolvePlannedChecks_CoverageRequested_AddsCollectToBaseTestCheck()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-collect-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "distill.yml"), """
                version: 1
                workspace:
                  solution: App.slnx
                  runDir: .distill/runs
                profiles:
                  quick:
                    checks:
                      - unit
                checks:
                  unit:
                    kind: test
                    command: dotnet test tests/App.Tests/App.Tests.csproj
                    source: auto
                    timeout: 60
                    stopOnFailure: true
                    dependsOn: []
                """);

            var config = DistillConfigLoader.Load(Path.Combine(root, "distill.yml"));
            var verificationPlan = new VerificationPlan(
                [
                    new PlannedVerificationCheck("unit", "test", "dotnet test tests/App.Tests/App.Tests.csproj"),
                    new PlannedVerificationCheck("runtime-coverage", "runtimecoverage", string.Empty)
                ],
                [],
                "quick");

            var resolved = DistillVerificationRunner.ResolvePlannedChecks(
                config, verificationPlan, root, Path.Combine(root, ".distill", "runs", "r1", "coverage"));

            var check = Assert.Single(resolved);
            Assert.Equal("unit", check.Id);
            Assert.Contains("--collect", check.Definition.Command);
            Assert.Contains("--results-directory:", check.Definition.Command);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolvePlannedChecks_CoverageRequested_CloneTestCheckCarriesCollect()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-collect-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "distill.yml"), """
                version: 1
                workspace:
                  solution: App.slnx
                  runDir: .distill/runs
                profiles:
                  quick:
                    checks:
                      - unit
                checks:
                  unit:
                    kind: test
                    command: dotnet test tests/App.Tests/App.Tests.csproj
                    source: auto
                    timeout: 60
                    stopOnFailure: true
                    dependsOn: []
                """);

            var config = DistillConfigLoader.Load(Path.Combine(root, "distill.yml"));
            var verificationPlan = new VerificationPlan(
                [
                    new PlannedVerificationCheck("runtime-coverage", "runtimecoverage", string.Empty),
                    new PlannedVerificationCheck(
                        "unit::abc12345",
                        "test",
                        "dotnet test tests/App.Tests/App.Tests.csproj",
                        ["O1"],
                        [],
                        "obligation-scoped-override",
                        null,
                        "App.OrderServiceTests")
                ],
                [],
                "quick");

            var resolved = DistillVerificationRunner.ResolvePlannedChecks(
                config, verificationPlan, root, Path.Combine(root, ".distill", "runs", "r1", "coverage"));

            var clone = Assert.Single(resolved, item => item.Id == "unit::abc12345");
            Assert.Contains("--collect", clone.Definition.Command);
            Assert.Contains("--results-directory:", clone.Definition.Command);
            Assert.Contains("FullyQualifiedName~App.OrderServiceTests", clone.Definition.Command);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolvePlannedChecks_NoCoverage_AddsNothing()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-collect-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "distill.yml"), """
                version: 1
                workspace:
                  solution: App.slnx
                  runDir: .distill/runs
                profiles:
                  quick:
                    checks:
                      - unit
                checks:
                  unit:
                    kind: test
                    command: dotnet test tests/App.Tests/App.Tests.csproj
                    source: auto
                    timeout: 60
                    stopOnFailure: true
                    dependsOn: []
                """);

            var config = DistillConfigLoader.Load(Path.Combine(root, "distill.yml"));
            var verificationPlan = new VerificationPlan(
                [new PlannedVerificationCheck("unit", "test", "dotnet test tests/App.Tests/App.Tests.csproj")],
                [],
                "quick");

            var resolved = DistillVerificationRunner.ResolvePlannedChecks(config, verificationPlan, root);

            var check = Assert.Single(resolved);
            Assert.DoesNotContain("--collect", check.Definition.Command);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Orchestrator_WiresCoverageProducer_ToCloseP005()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-orch-" + Guid.NewGuid().ToString("N"));
        try
        {
            const string xml = """
                <coverage><packages><package name="App"><classes>
                  <class name="App.OrderService"><methods>
                    <method name="Cancel"><lines><line number="1" hits="1" /></lines></method>
                  </methods></class>
                </classes></package></packages></coverage>
                """;
            Directory.CreateDirectory(Path.Combine(root, "coverage"));
            File.WriteAllText(Path.Combine(root, "coverage", "coverage.cobertura.xml"), xml);

            var runner = new CoverageRunner(["coverage/coverage.cobertura.xml"]);
            var orchestrator = new ProofOrchestrator(
                new SingleSymbolImpactProvider(),
                new DeterministicProofPlanner(),
                runner,
                new DeterministicProofEvaluator(),
                new ChangeCertificateBuilder(),
                runtimeCoverageEvidenceProducer: new RuntimeCoverageEvidenceProducer(),
                testMappingEvidenceProducer: new YamlTestMappingEvidenceProducer(null));

            var request = new ChangeRequest(root, "base", "head", [], SourceDigest: "src");
            var certificate = await orchestrator.VerifyAsync(
                request,
                "quick",
                CancellationToken.None,
                new ProofPolicy(TestMappingProjects: ["App"]),
                capabilities: DistillVerificationRunner.MergeProducerCapabilities([], "quick"));

            var p005 = Assert.Single(certificate.Evaluation.Obligations, item => item.Obligation.RuleId == "P005");
            Assert.True(p005.Obligation.Required);
            Assert.Equal(ObligationStatus.Proven, p005.Status);
            Assert.Equal(ProofVerdict.Proven, certificate.Verdict);
            Assert.Contains(certificate.Evidence.Evidence, item => item.Kind == EvidenceKind.RuntimeCoverage);

            var outOfScope = await orchestrator.VerifyAsync(
                request,
                "quick",
                CancellationToken.None,
                new ProofPolicy(TestMappingProjects: ["Other"]),
                capabilities: DistillVerificationRunner.MergeProducerCapabilities([], "quick"));

            var advisoryP005 = Assert.Single(outOfScope.Evaluation.Obligations, item => item.Obligation.RuleId == "P005");
            Assert.False(advisoryP005.Obligation.Required);
            Assert.DoesNotContain(
                outOfScope.Evidence.Evidence,
                item => item.Kind == EvidenceKind.RuntimeCoverage);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class CoverageRunner : IVerificationRunner
    {
        private readonly IReadOnlyList<string> _paths;

        public CoverageRunner(IReadOnlyList<string> paths) => _paths = paths;

        public Task<VerificationRunResult> VerifyAsync(
            ProofPlan plan,
            VerificationPlan verificationPlan,
            string workspaceRoot,
            CancellationToken cancellationToken)
        {
            // DistillVerificationRunner와 같다. 아티팩트는 검증 계획이
            // 이 실행에 대해 runtime-coverage를 선택했을 때만 존재한다.
            var coverageSelected = verificationPlan.Checks.Any(item =>
                string.Equals(item.CheckId, "runtime-coverage", StringComparison.OrdinalIgnoreCase));
            var artifacts = coverageSelected ? _paths : [];
            return Task.FromResult(new VerificationRunResult(new VerificationEvidenceSet([], []), artifacts));
        }
    }

    private sealed class SingleSymbolImpactProvider : IChangeImpactProvider
    {
        public Task<ChangeImpact> AnalyzeAsync(ChangeRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ChangeImpact(
                request.BaseRevision,
                request.HeadRevision,
                request.Spans,
                [new ChangedSymbolRef("sym-cancel", "App", "Services/OrderService.cs", "App.OrderService.Cancel", 1, 2, IsPublic: false, IsTest: false)],
                [],
                ["App"],
                [],
                "complete",
                false,
                SourceDigest: request.SourceDigest));
    }

    [Fact]
    public void CollectCoverageArtifacts_ReturnsRepoRelativePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-collect-" + Guid.NewGuid().ToString("N"));
        try
        {
            var coverage = Path.Combine(root, ".distill", "runs", "r1", "coverage", "abc");
            Directory.CreateDirectory(coverage);
            File.WriteAllText(Path.Combine(coverage, "coverage.cobertura.xml"), "<coverage />");

            var paths = DistillVerificationRunner.CollectCoverageArtifacts(
                root, Path.Combine(root, ".distill", "runs", "r1", "coverage"));

            var item = Assert.Single(paths);
            Assert.Equal(".distill/runs/r1/coverage/abc/coverage.cobertura.xml", item);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}