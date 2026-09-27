using Distill.Core.Config;
using Proof.Adapters.Distill;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

public sealed class DistillCommandRewriteTests
{
    [Fact]
    public void RewriteCommand_TestFilter_IsAppendedForTestVerb()
    {
        var rewritten = DistillVerificationRunner.RewriteCommand(
            "dotnet test tests/Foo.Tests/Foo.Tests.csproj --no-build",
            "C:/repo",
            null,
            "Bar");

        Assert.StartsWith("dotnet test tests/Foo.Tests/Foo.Tests.csproj", rewritten);
        Assert.Contains("--filter", rewritten);
        Assert.Contains("FullyQualifiedName~Bar", rewritten);
        Assert.Contains("--no-build", rewritten);
    }

    [Fact]
    public void RewriteCommand_TestFilter_IsIgnoredForNonTestVerb()
    {
        var rewritten = DistillVerificationRunner.RewriteCommand(
            "dotnet build App.slnx",
            "C:/repo",
            null,
            "Bar");

        Assert.DoesNotContain("--filter", rewritten);
    }

    [Fact]
    public void RewriteCommand_OverrideTarget_ResolvesProjectPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-rewrite-" + Guid.NewGuid().ToString("N"));
        try
        {
            var projectPath = Path.Combine(root, "src", "Proof.Core");
            Directory.CreateDirectory(projectPath);
            File.WriteAllText(Path.Combine(projectPath, "Proof.Core.csproj"), "<Project />");

            var resolved = DistillVerificationRunner.ResolveProjectPath(root, "Proof.Core");
            Assert.NotNull(resolved);
            Assert.EndsWith("Proof.Core.csproj", resolved!.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);

            var rewritten = DistillVerificationRunner.RewriteCommand("dotnet build Proof.slnx", root, "Proof.Core", null);
            Assert.EndsWith("Proof.Core.csproj", rewritten, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Proof.slnx", rewritten);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RewriteCommand_UnresolvableOverrideTarget_Throws()
    {
        // 해석할 수 없는 override가 솔루션 전역 대상을 조용히
        // 유지하면 절대 안 된다. 정확한 프로젝트 증거인 척하게 된다.
        var exception = Assert.Throws<DistillVerificationRunner.OverrideResolutionException>(
            () => DistillVerificationRunner.RewriteCommand(
                "dotnet build Proof.slnx",
                "C:/repo",
                "Does.Not.Exist",
                null));

        Assert.Contains("Does.Not.Exist", exception.Message);
    }

    [Fact]
    public void SplitBaseId_SeparatesCloneSuffix()
    {
        Assert.Equal("build", DistillVerificationRunner.SplitBaseId("build::App"));
        Assert.Equal("build", DistillVerificationRunner.SplitBaseId("build"));
    }

    [Fact]
    public void ResolvePlannedChecks_UnresolvableOverride_CloneIsSkipped()
    {
        // Wave B 3단계: OverrideTarget이 해석되지 않는 클론은
        // 버려야 한다. 의무는 정직하게 미적용으로 남고, 저장소 전역의
        // 정확한 클레임으로 낮아지지 않는다.
        var root = Path.Combine(Path.GetTempPath(), "proof-clone-" + Guid.NewGuid().ToString("N"));
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
                      - build
                checks:
                  build:
                    kind: build
                    command: dotnet build App.slnx
                    source: auto
                    timeout: 60
                    stopOnFailure: true
                    dependsOn: []
                """);

            var config = DistillConfigLoader.Load(Path.Combine(root, "distill.yml"));
            var verificationPlan = new VerificationPlan(
                [
                    new PlannedVerificationCheck("build", "build", "dotnet build App.slnx", null, null, "covers-required-or-dependency"),
                    new PlannedVerificationCheck(
                        "build::Missing",
                        "build",
                        "dotnet build App.slnx",
                        ["O1"],
                        null,
                        "obligation-scoped-override",
                        "Does.Not.Exist")
                ],
                [],
                "quick");

            var resolved = DistillVerificationRunner.ResolvePlannedChecks(config, verificationPlan, root);

            var check = Assert.Single(resolved);
            Assert.Equal("build", check.Id);
            Assert.DoesNotContain(resolved, item => item.Id == "build::Missing");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
