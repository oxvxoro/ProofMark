using Proof.Cli;
using Proof.Core;

namespace Proof.Tests;

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
