using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

public sealed class ScopedVerificationPlannerTests
{
    [Fact]
    public void ProjectObligation_WithSolutionBuildCheck_ProducesScopedClone()
    {
        var plan = new ProofPlan([
            new ProofObligation(
                "O1", "P003", ObligationKind.CrossProject, "build", "App", true, 3, ["r"],
                new ProofSubject(SubjectKind.Project, "App", "App"))
        ]);
        var catalog = new[]
        {
            new EvidenceCapability("build", "build", "App.slnx", ScopeMode.RepositoryWide)
        };

        var verificationPlan = new VerificationPlanner().Plan(plan, catalog, "quick");

        Assert.Empty(verificationPlan.Uncovered);
        var clone = Assert.Single(verificationPlan.Checks);
        Assert.Equal("build::App", clone.CheckId);
        Assert.Equal("App", clone.OverrideTarget);
        Assert.Equal("obligation-scoped-override", clone.SelectionReason);
    }

    [Fact]
    public void TestObligation_WithRepositoryWideTestCheck_ProducesFilteredClone()
    {
        var plan = new ProofPlan([
            new ProofObligation(
                "O1", "P004", ObligationKind.Test, "impacted", "t1", true, 4, ["r"],
                new ProofSubject(
                    SubjectKind.Test,
                    "t1",
                    "App.Tests",
                    "tests/App.Tests/OrderServiceTests.cs",
                    "App.Tests.OrderServiceTests.Cancel_Works"))
        ]);
        var catalog = new[]
        {
            new EvidenceCapability("unit", "test", "App.slnx", ScopeMode.RepositoryWide)
        };

        var verificationPlan = new VerificationPlanner().Plan(plan, catalog, "quick");

        Assert.Empty(verificationPlan.Uncovered);
        var clone = Assert.Single(verificationPlan.Checks);
        Assert.StartsWith("unit::", clone.CheckId);
        Assert.Equal("App.Tests.OrderServiceTests", clone.TestFilter);
    }

    [Fact]
    public void ExactProjectTarget_NeedsNoOverride()
    {
        var plan = new ProofPlan([
            new ProofObligation(
                "O1", "P003", ObligationKind.CrossProject, "build", "App", true, 3, ["r"],
                new ProofSubject(SubjectKind.Project, "App", "App"))
        ]);
        var catalog = new[]
        {
            new EvidenceCapability("build", "build", "src/App/App.csproj", ScopeMode.Exact)
        };

        var verificationPlan = new VerificationPlanner().Plan(plan, catalog, "quick");

        Assert.Empty(verificationPlan.Uncovered);
        var check = Assert.Single(verificationPlan.Checks);
        Assert.Equal("build", check.CheckId);
        Assert.Null(check.OverrideTarget);
    }

    [Fact]
    public void BuildAndTestOverrides_WireCloneDependsOnToBuildClone()
    {
        var plan = new ProofPlan([
            new ProofObligation(
                "O1", "P003", ObligationKind.CrossProject, "build", "App", true, 3, ["r"],
                new ProofSubject(SubjectKind.Project, "App", "App")),
            new ProofObligation(
                "O2", "P004", ObligationKind.Test, "impacted", "t1", true, 4, ["r"],
                new ProofSubject(
                    SubjectKind.Test,
                    "t1",
                    "App.Tests",
                    "tests/App.Tests/OrderServiceTests.cs",
                    "App.Tests.OrderServiceTests.Cancel_Works"))
        ]);
        var catalog = new[]
        {
            new EvidenceCapability("build", "build", "App.slnx", ScopeMode.RepositoryWide),
            new EvidenceCapability("unit", "test", "App.slnx", ScopeMode.RepositoryWide, DependsOn: ["build"])
        };

        var verificationPlan = new VerificationPlanner().Plan(plan, catalog, "quick");

        Assert.Empty(verificationPlan.Uncovered);
        var buildClone = Assert.Single(verificationPlan.Checks, item => item.CheckId == "build::App");
        var testClone = Assert.Single(verificationPlan.Checks, item => item.CheckId.StartsWith("unit::", StringComparison.Ordinal));
        Assert.DoesNotContain(verificationPlan.Checks, item => item.CheckId == "build");
        Assert.Contains("build::App", testClone.DependsOn ?? []);
        Assert.DoesNotContain("build", testClone.DependsOn ?? [], StringComparer.Ordinal);
        Assert.Equal("App", buildClone.OverrideTarget);
        Assert.Equal("App.Tests.OrderServiceTests", testClone.TestFilter);
    }

    [Fact]
    public void NoMatchingCapability_LeavesObligationUncovered()
    {
        var plan = new ProofPlan([
            new ProofObligation(
                "O1", "P003", ObligationKind.CrossProject, "build", "App", true, 3, ["r"],
                new ProofSubject(SubjectKind.Project, "App", "App"))
        ]);
        var catalog = new[]
        {
            new EvidenceCapability("lint", "analysis", null, ScopeMode.RepositoryWide)
        };

        var verificationPlan = new VerificationPlanner().Plan(plan, catalog, "quick");

        Assert.Contains(verificationPlan.Uncovered, item => item.ObligationId == "O1");
        Assert.Empty(verificationPlan.Checks);
    }

    [Fact]
    public void ObligationEvidenceLinks_StayEmpty_WhenPlanSelectsNothing()
    {
        // 미적용 의무는 계획되지 않은 검사에 귀속된 증거를 바인딩하면 안 된다.
        var plan = new ProofPlan([
            new ProofObligation(
                "O1", "P003", ObligationKind.CrossProject, "build", "App", true, 3, ["r"],
                new ProofSubject(SubjectKind.Project, "App", "App"))
        ]);
        var catalog = new[]
        {
            new EvidenceCapability("lint", "analysis", null, ScopeMode.RepositoryWide)
        };
        var verificationPlan = new VerificationPlanner().Plan(plan, catalog, "quick");
        var evidence = new[]
        {
            new ProofEvidence(
                "E1",
                EvidenceKind.Build,
                "App",
                EvidenceStatus.Pass,
                new EvidenceProvenance("distill", CheckId: "build"),
                new EvidenceScope(ScopeMode.RepositoryWide, CommandTarget: "App.slnx"))
        };

        var bound = new EvidenceBinder().Bind(plan, evidence, verificationPlan);

        Assert.Empty(bound.Links);
    }

    [Fact]
    public void OverrideDonor_PrefersCheaperCheck_AtEqualScope()
    {
        var plan = new ProofPlan([
            new ProofObligation(
                "O1", "P004", ObligationKind.Test, "impacted", "t1", true, 4, ["r"],
                new ProofSubject(
                    SubjectKind.Test, "t1", "App.Tests", "tests/App.Tests/OrderServiceTests.cs",
                    "App.Tests.OrderServiceTests.Cancel_Works"))
        ]);
        var catalog = new[]
        {
            new EvidenceCapability("unit-expensive", "test", "App.slnx", ScopeMode.RepositoryWide, Cost: 5),
            new EvidenceCapability("unit-cheap", "test", "App.slnx", ScopeMode.RepositoryWide, Cost: 2)
        };

        var verificationPlan = new VerificationPlanner().Plan(plan, catalog, "quick");

        Assert.Contains(verificationPlan.Checks, item => item.CheckId.StartsWith("unit-cheap::", StringComparison.Ordinal));
        Assert.DoesNotContain(verificationPlan.Checks, item => item.CheckId.StartsWith("unit-expensive::", StringComparison.Ordinal));
    }

    [Fact]
    public void SameClassTestObligations_ShareOneClone()
    {
        var plan = new ProofPlan([
            TestObligation("O1", "t1", "App.Tests.OrderServiceTests.Cancel_Works"),
            TestObligation("O2", "t2", "App.Tests.OrderServiceTests.Refund_Works"),
            TestObligation("O3", "t3", "App.Tests.BillingServiceTests.Refund_Works")
        ]);
        var catalog = new[]
        {
            new EvidenceCapability("unit", "test", "App.slnx", ScopeMode.RepositoryWide)
        };

        var verificationPlan = new VerificationPlanner().Plan(plan, catalog, "quick");

        var clones = verificationPlan.Checks.Where(item => item.CheckId.StartsWith("unit::", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, clones.Length);
        var orderClone = Assert.Single(clones, item => item.TestFilter == "App.Tests.OrderServiceTests");
        Assert.Equal(["O1", "O2"], orderClone.SatisfiesObligationIds);
        Assert.Contains(clones, item => item.TestFilter == "App.Tests.BillingServiceTests");
    }

    [Fact]
    public void EqualCostOverrides_PickSameDonorRegardlessOfCatalogOrder()
    {
        var plan = new ProofPlan([
            new ProofObligation(
                "O1", "P004", ObligationKind.Test, "impacted", "t1", true, 4, ["r"],
                new ProofSubject(
                    SubjectKind.Test, "t1", "App.Tests", "tests/App.Tests/OrderServiceTests.cs",
                    "App.Tests.OrderServiceTests.Cancel_Works"))
        ]);
        var first = new EvidenceCapability("unit-a", "test", "App.slnx", ScopeMode.RepositoryWide);
        var second = new EvidenceCapability("unit-b", "test", "App.slnx", ScopeMode.RepositoryWide);

        var forward = new VerificationPlanner().Plan(plan, [first, second], "quick");
        var reverse = new VerificationPlanner().Plan(plan, [second, first], "quick");

        Assert.Equal(
            forward.Checks.Select(item => item.CheckId).ToArray(),
            reverse.Checks.Select(item => item.CheckId).ToArray());
        Assert.All(forward.Checks, item => Assert.StartsWith("unit-a::", item.CheckId, StringComparison.Ordinal));
    }

    private static ProofObligation TestObligation(string id, string subjectId, string displayName)
        => new(
            id, "P004", ObligationKind.Test, "impacted", subjectId, true, 4, ["r"],
            new ProofSubject(SubjectKind.Test, subjectId, "App.Tests", "tests/App.Tests/X.cs", displayName));
}
