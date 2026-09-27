using Distill.Core.Config;
using Distill.Core.Evidence;
using Distill.Core.Planning;
using Distill.Core.Runs;
using Proof.Adapters.Distill;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

public sealed class TestCaseProjectIdentityTests
{
    [Theory]
    [InlineData("Tests/Tests.csproj", "Tests")]
    [InlineData(@"C:\repo\Tests\App.Tests.csproj", "App.Tests")]
    [InlineData("Lib/Lib.fsproj", "Lib")]
    [InlineData("App.Tests", "App.Tests")]
    [InlineData(null, null)]
    public void ProjectName_ReducesProjectFilePathsToTheProjectName(string? project, string? expected)
        => Assert.Equal(expected, DistillEvidenceMapper.ProjectName(project));

    [Fact]
    public void TestCaseFromProjectPath_DirectlyClosesImpactedTestAndTestCaller()
    {
        const string fqn = "SimpleService.Tests.OrderServiceTests.Cancel_RefundsPayment";
        const string symbolId = "sym://M:" + fqn;
        var subject = new ProofSubject(SubjectKind.Test, symbolId, "Tests", "OrderServiceTests.cs", fqn + "()");
        var plan = new ProofPlan(
        [
            new ProofObligation("O-P004", "P004", ObligationKind.Test, "claim", symbolId, true, 4, ["r"], subject),
            new ProofObligation("O-P002", "P002", ObligationKind.CallerContract, "claim", symbolId, true, 4, ["r"], subject with { Kind = SubjectKind.Symbol })
        ], SourceDigest: "src");
        var check = new CheckConfig { Kind = "test", Command = "dotnet test Tests/Tests.csproj --no-build" };
        var result = new CheckRunResult(
            "unit", "test", VerificationStatus.Pass, 0, [], null, null, TimeSpan.Zero,
            [new TestCaseEvidence(fqn, "Passed", null, null, 1, fqn, "Tests/Tests.csproj")]);

        var evidence = DistillEvidenceMapper.Map(plan, [new PlannedCheck("unit", check, [])], [result]);
        var bound = new EvidenceBinder().Bind(plan, evidence);

        Assert.Contains(bound.Links, link => link.ObligationId == "O-P004" && link.BindingRuleId == "BIND_TEST_CASE" && link.Relation == "direct");
        Assert.Contains(bound.Links, link => link.ObligationId == "O-P002" && link.BindingRuleId == "BIND_CALLER_TEST" && link.Relation == "direct");
    }
}
