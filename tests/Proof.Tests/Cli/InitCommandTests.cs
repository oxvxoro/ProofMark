using Proof.Cli;

namespace Proof.Tests;

public sealed class InitCommandTests
{
    [Fact]
    public void Init_PreservesExistingProofYml()
    {
        var root = CreateWorkspace();
        var configPath = Path.Combine(root, "proof.yml");
        File.WriteAllText(configPath, "version: 2\nproof:\n  base:\n    strategy: mergeBase\n    ref: origin/main\n");
        try
        {
            var code = InitCommand.Execute(force: false, workspaceRoot: root);
            Assert.Equal(0, code);
            Assert.Equal("version: 2\nproof:\n  base:\n    strategy: mergeBase\n    ref: origin/main\n", File.ReadAllText(configPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Init_CreatesDistillYmlWhenMissing()
    {
        var root = CreateWorkspace();
        var example = Path.Combine(root, "proof.yml.example");
        File.WriteAllText(
            example,
            """
            version: 2
            proof:
              base:
                strategy: mergeBase
                ref: origin/main
            analysis:
              solution: YourSolution.slnx
            verification:
              distillConfig: distill.yml
              distillProfile: quick
            """);
        File.WriteAllText(Path.Combine(root, "App.slnx"), "<Solution />");
        try
        {
            var code = InitCommand.Execute(force: false, workspaceRoot: root);
            Assert.Equal(0, code);
            Assert.True(File.Exists(Path.Combine(root, "distill.yml")));
            var proofYml = File.ReadAllText(Path.Combine(root, "proof.yml"));
            Assert.Contains("solution: App.slnx", proofYml, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Init_PreservesExistingDistillYml()
    {
        var root = CreateWorkspace();
        var example = Path.Combine(root, "proof.yml.example");
        File.WriteAllText(
            example,
            """
            version: 2
            proof:
              base:
                strategy: mergeBase
                ref: origin/main
            verification:
              distillConfig: distill.yml
              distillProfile: quick
            """);
        var existingDistill = Path.Combine(root, "distill.yml");
        File.WriteAllText(existingDistill, "version: 1\nworkspace:\n  solution: keep.sln\n");
        try
        {
            var code = InitCommand.Execute(force: false, workspaceRoot: root);
            Assert.Equal(0, code);
            Assert.Equal("version: 1\nworkspace:\n  solution: keep.sln\n", File.ReadAllText(existingDistill));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Init_CopiesExampleWhenMissing()
    {
        var root = CreateWorkspace();
        var example = Path.Combine(root, "proof.yml.example");
        File.WriteAllText(
            example,
            """
            version: 2
            proof:
              base:
                strategy: mergeBase
                ref: origin/main
            verification:
              distillConfig: distill.yml
              distillProfile: quick
            """);
        try
        {
            var code = InitCommand.Execute(force: false, workspaceRoot: root);
            Assert.Equal(0, code);
            Assert.True(File.Exists(Path.Combine(root, "proof.yml")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RepositoryProofYml_BaseRef_IsOriginMain()
    {
        var root = FindRepoRoot();
        var config = ProofConfig.LoadedFromFile(Path.Combine(root, "proof.yml"), root);
        Assert.Equal("mergeBase", config.Proof.Base.Strategy);
        Assert.Equal("origin/main", config.Proof.Base.Ref);
    }

    [Fact]
    public void ProofYmlExample_ParsesAndValidatesTestMaps()
    {
        var path = Path.Combine(FindRepoRoot(), "proof.yml.example");
        var config = ProofConfig.LoadedFromFile(path, workspaceRoot: null);
        ConfigValidateCommand.ValidateTestMaps(config);
        Assert.Equal("off", config.Policy.Architecture);
        Assert.Equal("off", config.Policy.AppContract);
        Assert.Equal("code", config.Analysis.Impact.Profile);
    }

    private static string CreateWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-init-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "Proof.slnx")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
