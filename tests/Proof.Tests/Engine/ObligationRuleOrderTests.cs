using Proof.Engine.Planning;

namespace Proof.Tests;

public sealed class ObligationRuleOrderTests
{
    [Fact]
    public void ObligationRules_AreRegisteredInFixedOrder()
    {
        string[] expected =
        [
            "PublicApi",
            "CallerContract",
            "CrossProject",
            "Test",
            "Capture",
            "Uncertainty",
            "AppContract",
            "Architecture",
            "StaticAnalysis",
        ];

        var actual = ObligationRuleRegistry.Rules.Select(rule => rule.RuleId).ToArray();
        Assert.Equal(expected, actual);
    }
}
