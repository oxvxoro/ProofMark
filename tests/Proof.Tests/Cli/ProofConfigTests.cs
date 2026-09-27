using Proof.Adapters.CodeMap;
using Proof.Core;
using Proof.Cli;
using Proof.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Proof.Tests;

public sealed class ProofConfigTests
{
    [Fact]
    public void Load_ParsesAnalysisSolutionRelativeToWorkspace()
    {
        var root = CreateWorkspace(out var solutionPath);
        try
        {
            File.WriteAllText(Path.Combine(root, "proof.yml"), """
                version: 2
                proof:
                  base:
                    strategy: mergeBase
                verification:
                  distillProfile: quick
                analysis:
                  solution: App.slnx
                  indexBaseRevision: true
                """);

            var config = ProofConfig.Load(root);

            Assert.Equal("App.slnx", config.Analysis.Solution);
            Assert.True(config.Analysis.IndexBaseRevision);
            Assert.Equal(solutionPath, AnalysisTargetResolver.Resolve(root, config.Analysis.Solution));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_IndexBaseRevisionDefaultsToFalse_AndRejectsUnknownAnalysisKeys()
    {
        var root = CreateWorkspace(out _);
        try
        {
            File.WriteAllText(Path.Combine(root, "proof.yml"), """
                version: 2
                verification:
                  distillProfile: quick
                """);
            var config = ProofConfig.Load(root);
            Assert.False(config.Analysis.IndexBaseRevision);

            File.WriteAllText(Path.Combine(root, "proof.yml"), """
                version: 2
                verification:
                  distillProfile: quick
                analysis:
                  indexBaseRevisions: true
                """);
            Assert.Throws<global::Proof.Core.ProofConfigException>(() => ProofConfig.Load(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_NormalizesMixedSeparators()
    {
        var root = CreateWorkspace(out var solutionPath, nested: true);
        try
        {
            File.WriteAllText(Path.Combine(root, "proof.yml"), """
                version: 2
                proof:
                  base:
                    strategy: mergeBase
                verification:
                  distillProfile: quick
                analysis:
                  solution: nested\App.slnx
                """);

            var resolved = AnalysisTargetResolver.Resolve(root, ProofConfig.Load(root).Analysis.Solution);
            Assert.Equal(solutionPath, resolved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void UnknownAnalysisProperty_IsRejected()
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        var yaml = """
            version: 2
            analysis:
              solution: Proof.slnx
              impactDepth: 2
            """;
        var exception = Assert.Throws<ProofConfigException>(() => ProofConfig.RejectUnknownProperties(yaml, deserializer));
        Assert.Contains("impactDepth", exception.Message);
    }

    [Fact]
    public void InvalidSolutionExtension_IsRejected()
    {
        var exception = Assert.Throws<ProofConfigException>(() =>
            new ProofConfig { Analysis = { Solution = "notes.txt" } }.Validate());
        Assert.Contains("extension", exception.Message);
    }

    [Fact]
    public void SolutionOutsideWorkspace_IsRejected()
    {
        var root = CreateWorkspace(out _);
        var outside = Path.Combine(Path.GetTempPath(), "proof-outside-" + Guid.NewGuid(), "Other.slnx");
        Directory.CreateDirectory(Path.GetDirectoryName(outside)!);
        File.WriteAllText(outside, "<Solution />");
        try
        {
            var exception = Assert.Throws<ProofConfigException>(() =>
                AnalysisTargetResolver.Resolve(root, outside));
            Assert.Contains("inside the workspace", exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(Path.GetDirectoryName(outside)!, recursive: true);
        }
    }

    [Fact]
    public void MissingSolutionFile_IsRejected()
    {
        var root = CreateWorkspace(out _);
        try
        {
            var exception = Assert.Throws<ProofConfigException>(() =>
                AnalysisTargetResolver.Resolve(root, "Missing.slnx"));
            Assert.Contains("not found", exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveIndexInput_UsesExplicitSolutionPath()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "proof-index-" + Guid.NewGuid());
        var solution = Path.Combine(workspace, "Proof.slnx");
        Assert.Equal(Path.GetFullPath(solution), CodeMapChangeImpactProvider.ResolveIndexInput(workspace, solution));
        Assert.Equal(Path.GetFullPath(workspace), CodeMapChangeImpactProvider.ResolveIndexInput(workspace, null));
    }

    [Fact]
    public void ResolveBudget_UsesPublicDepthForPublicSymbols()
    {
        var request = new ChangeRequest("root", "base", "head", [new LineSpan("a.cs", 1, 2)]);
        var publicBudget = CodeMapChangeImpactProvider.ResolveBudget(
            request,
            [new ChangedSymbolRef("s1", "App", "a.cs", "Api", 1, 2, true, false)],
            new ImpactAnalysisSettings(BaseDepth: 2, PublicDepth: 4, MaxResults: 10, CallerPageSize: 5, CallerMaxResults: 20));
        var privateBudget = CodeMapChangeImpactProvider.ResolveBudget(
            request,
            [new ChangedSymbolRef("s1", "App", "a.cs", "Impl", 1, 2, false, false)],
            new ImpactAnalysisSettings(BaseDepth: 2, PublicDepth: 4, MaxResults: 10, CallerPageSize: 5, CallerMaxResults: 20));
        Assert.Equal(4, publicBudget.Depth);
        Assert.Equal(2, privateBudget.Depth);
        Assert.Equal(20, publicBudget.CallerMaxResults);
    }

    [Fact]
    public void ImpactProfile_IsParsed_AndValidated()
    {
        var app = new ProofConfig { Analysis = { Impact = { Profile = "app" } } };
        app.Validate();
        Assert.Equal("app", app.ToImpactSettings().Profile);

        var exception = Assert.Throws<ProofConfigException>(() =>
            new ProofConfig { Analysis = { Impact = { Profile = "bogus" } } }.Validate());
        Assert.Contains("analysis.impact.profile", exception.Message);
    }

    [Fact]
    public void AppContract_IsParsed_AndValidated()
    {
        var required = new ProofConfig { Policy = { AppContract = "required" } };
        required.Validate();
        Assert.True(required.ToPolicy().AppContractRequired);

        Assert.False(new ProofConfig().ToPolicy().AppContractRequired);

        var exception = Assert.Throws<ProofConfigException>(() =>
            new ProofConfig { Policy = { AppContract = "advisory" } }.Validate());
        Assert.Contains("policy.appContract", exception.Message);
    }

    [Fact]
    public void Architecture_IsParsed_AndValidated()
    {
        Assert.Equal(ArchitecturePolicyMode.Required, new ProofConfig { Policy = { Architecture = "required" } }.ToPolicy().Architecture);
        Assert.Equal(ArchitecturePolicyMode.Advisory, new ProofConfig { Policy = { Architecture = "advisory" } }.ToPolicy().Architecture);
        Assert.Equal(ArchitecturePolicyMode.Off, new ProofConfig().ToPolicy().Architecture);

        var exception = Assert.Throws<ProofConfigException>(() =>
            new ProofConfig { Policy = { Architecture = "sometimes" } }.Validate());
        Assert.Contains("policy.architecture", exception.Message);
        Assert.Contains("off", exception.Message);
        Assert.Contains("advisory", exception.Message);
        Assert.Contains("required", exception.Message);
    }

    [Fact]
    public void Load_AcceptsArchitectureKey_AndStillRejectsUnknownPolicyKeys()
    {
        var root = CreateWorkspace(out _);
        try
        {
            File.WriteAllText(Path.Combine(root, "proof.yml"), """
                version: 2
                verification:
                  distillProfile: quick
                policy:
                  architecture: advisory
                """);
            var config = ProofConfig.Load(root);
            Assert.Equal(ArchitecturePolicyMode.Advisory, config.ToPolicy().Architecture);

            File.WriteAllText(Path.Combine(root, "proof.yml"), """
                version: 2
                verification:
                  distillProfile: quick
                policy:
                  architecturex: advisory
                """);
            var exception = Assert.Throws<ProofConfigException>(() => ProofConfig.Load(root));
            Assert.Contains("Unknown configuration property 'policy.architecturex'", exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void VerificationCache_IsAcceptedAndParsed()
    {
        var config = new ProofConfig { Verification = { Cache = true } };
        config.Validate();
        Assert.True(config.Verification.Cache);
        Assert.False(new ProofConfig().Verification.Cache);
    }

    private static string CreateWorkspace(out string solutionPath, bool nested = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-config-" + Guid.NewGuid());
        solutionPath = nested
            ? Path.Combine(root, "nested", "App.slnx")
            : Path.Combine(root, "App.slnx");
        Directory.CreateDirectory(Path.GetDirectoryName(solutionPath)!);
        File.WriteAllText(solutionPath, "<Solution />");
        return root;
    }
}
