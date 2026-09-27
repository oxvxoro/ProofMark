using Distill.Core.Config;
using Distill.Core.Evidence;
using Distill.Core.Planning;
using Distill.Core.Runs;
using Proof.Adapters.Distill;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

public sealed class ProcessEvidenceTests : IDisposable
{
    private const string ScipTest = "sym://scip:web/tests.cart.adds_item";
    private const string CsTest = "sym://M:App.Tests.CartTests.AddsItem";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"proof-process-{Guid.NewGuid():N}");

    public ProcessEvidenceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void ProcessExitCode_NeverBindsDirectlyAndDropsTestCases()
    {
        var symbol = new ProofSubject(SubjectKind.Symbol, "sym://M:App.Cart.Add", "App", "Cart.cs", "App.Cart.Add()");
        var plan = new ProofPlan(
        [
            new ProofObligation("O-P005", "P005", ObligationKind.TestMapping, "claim", symbol.Id, true, 4, ["r"], symbol),
            new ProofObligation("O-P009", "P009", ObligationKind.ManualReview, "claim", "src/App/Cart.cs", true, 4, ["r"]),
            new ProofObligation("O-P004", "P004", ObligationKind.Test, "claim", CsTest, true, 4, ["r"], TestSubject(CsTest, "App.Tests", "App.Tests.CartTests.AddsItem"))
        ], SourceDigest: "src");
        var check = new CheckConfig { Kind = "process", Command = "./run.sh src/App/Cart.cs App.Tests.csproj" };
        var result = new CheckRunResult(
            "script", "process", VerificationStatus.Pass, 0, [], "auto", null, TimeSpan.Zero,
            [new TestCaseEvidence("AddsItem", "Passed", null, null, 1, "App.Tests.CartTests.AddsItem", "App.Tests/App.Tests.csproj")]);

        var evidence = DistillEvidenceMapper.Map(plan, [new PlannedCheck("script", check, [])], [result]);
        var bound = new EvidenceBinder().Bind(plan, evidence);

        var single = Assert.Single(evidence);
        Assert.Equal(EvidenceKind.ManualReview, single.Kind);
        Assert.Null(single.Scope?.CommandTarget);
        Assert.Null(single.Scope?.SubjectRefs);
        Assert.DoesNotContain(bound.Links, link => link.Relation == "direct");
    }

    [Fact]
    public void JUnitCases_CloseScipTestObligationOnly()
    {
        var plan = new ProofPlan(
        [
            new ProofObligation("O-SCIP", "P004", ObligationKind.Test, "claim", ScipTest, true, 4, ["r"], TestSubject(ScipTest, "scip:web", "tests.cart.adds_item")),
            new ProofObligation("O-CS", "P004", ObligationKind.Test, "claim", CsTest, true, 4, ["r"], TestSubject(CsTest, "App.Tests", "tests.cart.adds_item"))
        ], SourceDigest: "src");
        var check = new CheckConfig
        {
            Kind = "process", Command = "npm test", Source = "junit", Artifact = "web/junit.xml", Project = "scip:web"
        };
        var result = new CheckRunResult(
            "web", "process", VerificationStatus.Pass, 0, [], "junit", null, TimeSpan.Zero,
            [new TestCaseEvidence("adds_item", "Passed", null, null, 1, "tests.cart.adds_item", "scip:web")]);

        var evidence = DistillEvidenceMapper.Map(plan, [new PlannedCheck("web", check, [])], [result]);
        var bound = new EvidenceBinder().Bind(plan, evidence);

        Assert.Contains(bound.Links, link => link.ObligationId == "O-SCIP" && link.BindingRuleId == "BIND_TEST_CASE" && link.Relation == "direct");
        Assert.DoesNotContain(bound.Links, link => link.ObligationId == "O-CS" && link.Relation == "direct");
    }

    [Fact]
    public void Catalog_AdvertisesOnlyResolvedScipJUnitAsExactTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "web"));
        File.WriteAllText(Path.Combine(_root, "distill.yml"), """
            version: 1
            profiles:
              quick:
                checks: [web, missing, script, unit]
            checks:
              web:
                kind: process
                command: npm test
                source: junit
                artifact: web/junit.xml
                project: scip:web
              missing:
                kind: process
                command: npm test
                source: junit
                artifact: api/junit.xml
                project: scip:api
              script:
                kind: process
                command: ./run App.Tests.csproj
              unit:
                kind: test
                command: dotnet test App.sln
            """);

        var catalog = DistillCapabilitySource.BuildCatalog(_root).ToDictionary(item => item.CheckId);

        Assert.Equal(("test", "scip:web", ScopeMode.Exact), (catalog["web"].Kind, catalog["web"].CommandTarget, catalog["web"].ScopeMode));
        Assert.Equal(("process", null, ScopeMode.RepositoryWide), (catalog["missing"].Kind, catalog["missing"].CommandTarget, catalog["missing"].ScopeMode));
        Assert.Equal(("process", null, ScopeMode.RepositoryWide), (catalog["script"].Kind, catalog["script"].CommandTarget, catalog["script"].ScopeMode));
    }

    [Fact]
    public void Planner_UsesScipJUnitOnlyForItsProjectAndNeverAsOverrideDonor()
    {
        var plan = new ProofPlan(
        [
            new ProofObligation("O-SCIP", "P004", ObligationKind.Test, "claim", ScipTest, true, 4, ["r"], TestSubject(ScipTest, "scip:web", "tests.cart.adds_item")),
            new ProofObligation("O-CS", "P004", ObligationKind.Test, "claim", CsTest, true, 4, ["r"], TestSubject(CsTest, "App.Tests", "App.Tests.CartTests.AddsItem")),
            new ProofObligation("O-P009", "P009", ObligationKind.ManualReview, "claim", "src/App/Cart.cs", true, 4, ["r"])
        ], SourceDigest: "src");
        EvidenceCapability[] catalog =
        [
            new("web", "test", "scip:web", ScopeMode.Exact, null, 3),
            new("script", "process", null, ScopeMode.RepositoryWide, null, 1),
            new("unit", "test", null, ScopeMode.RepositoryWide, null, 3)
        ];

        var verification = new VerificationPlanner().Plan(plan, catalog, "quick");

        var web = Assert.Single(verification.Checks, item => item.CheckId == "web");
        Assert.Equal(["O-SCIP"], web.SatisfiesObligationIds);
        Assert.DoesNotContain(verification.Checks, item => item.CheckId.StartsWith("web::", StringComparison.Ordinal));
        Assert.Contains(verification.Checks, item => item.CheckId.StartsWith("unit::", StringComparison.Ordinal)
                                                     && item.SatisfiesObligationIds!.Contains("O-CS"));
        Assert.DoesNotContain(verification.Checks, item => item.CheckId == "script");
        Assert.Contains(verification.Uncovered, item => item.ObligationId == "O-P009");
    }

    [Fact]
    public void Compiler_NeverRewritesProcessCommands()
    {
        var config = DistillConfigLoader.LoadFromYaml("""
            version: 1
            profiles:
              quick:
                checks: [web]
            checks:
              web:
                kind: process
                command: npm test
            """);
        var verificationPlan = new VerificationPlan(
            [
                new PlannedVerificationCheck("web", "process", string.Empty),
                new PlannedVerificationCheck("web::abc", "process", string.Empty, OverrideTarget: "App", TestFilter: "App.Tests")
            ],
            [],
            "quick");

        var resolved = DistillVerificationRunner.ResolvePlannedChecks(
            config, verificationPlan, _root, Path.Combine(_root, "coverage"));

        var check = Assert.Single(resolved);
        Assert.Equal("web", check.Id);
        Assert.Equal("npm test", check.Definition.Command);
    }

    [Fact]
    public void ConfiguredCoverage_PassesOnlyFreshFilesFromChecksThatRan()
    {
        var startedAt = DateTimeOffset.UtcNow;
        Directory.CreateDirectory(Path.Combine(_root, "web"));
        File.WriteAllText(Path.Combine(_root, "web", "fresh.xml"), "<coverage />");
        var stale = Path.Combine(_root, "web", "stale.xml");
        File.WriteAllText(stale, "<coverage />");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-1));

        PlannedCheck Process(string id, string coverage)
            => new(id, new CheckConfig { Kind = "process", Command = "npm test", Coverage = coverage }, []);
        CheckRunResult Ran(string id, VerificationStatus status)
            => new(id, "process", status, 0, [], null, null, TimeSpan.Zero);

        var paths = DistillExecutionGateway.CollectConfiguredCoverage(
            _root,
            [
                Process("fresh", "web/fresh.xml"),
                Process("stale", "web/stale.xml"),
                Process("missing", "web/missing.xml"),
                Process("infra", "web/fresh.xml")
            ],
            [
                Ran("fresh", VerificationStatus.Fail),
                Ran("stale", VerificationStatus.Pass),
                Ran("missing", VerificationStatus.Pass),
                Ran("infra", VerificationStatus.InfraError)
            ],
            startedAt);

        Assert.Equal(["web/fresh.xml"], paths);
    }

    private static ProofSubject TestSubject(string id, string project, string displayName)
        => new(SubjectKind.Test, id, project, "tests/cart.test.js", displayName);
}
