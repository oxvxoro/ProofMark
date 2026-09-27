using Distill.Core.Config;

namespace Distill.Tests.Core;

public class DistillConfigLoaderStrictTests
{
    [Fact]
    public void LoadFromYaml_UnknownRootKey_Throws()
    {
        const string yaml = """
            version: 1
            typoField: true
            profiles:
              quick:
                checks: [build]
            checks:
              build:
                kind: build
                command: dotnet build App.sln
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => DistillConfigLoader.LoadFromYaml(yaml));
        Assert.Contains("typoField", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadFromYaml_UnknownDependency_Throws()
    {
        const string yaml = """
            version: 1
            profiles:
              quick:
                checks: [unit]
            checks:
              unit:
                kind: test
                command: dotnet test App.Tests.csproj
                dependsOn: [missing]
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => DistillConfigLoader.LoadFromYaml(yaml));
        Assert.Contains("unknown check 'missing'", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadFromYaml_DependencyCycle_Throws()
    {
        const string yaml = """
            version: 1
            profiles:
              quick:
                checks: [a]
            checks:
              a:
                kind: build
                command: dotnet build A.sln
                dependsOn: [b]
              b:
                kind: build
                command: dotnet build B.sln
                dependsOn: [a]
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => DistillConfigLoader.LoadFromYaml(yaml));
        Assert.Contains("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadFromYaml_NonDefaultRunDir_Throws()
    {
        const string yaml = """
            version: 1
            workspace:
              runDir: /tmp/custom
            profiles:
              quick:
                checks: [build]
            checks:
              build:
                kind: build
                command: dotnet build App.sln
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => DistillConfigLoader.LoadFromYaml(yaml));
        Assert.Contains("runDir", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadFromYaml_NonPositiveTimeout_Throws()
    {
        const string yaml = """
            version: 1
            profiles:
              quick:
                checks: [build]
            checks:
              build:
                kind: build
                command: dotnet build App.sln
                timeout: 0
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => DistillConfigLoader.LoadFromYaml(yaml));
        Assert.Contains("timeout", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadFromYaml_EmptyProfileChecks_Throws()
    {
        const string yaml = """
            version: 1
            profiles:
              quick:
                checks: []
            checks:
              build:
                kind: build
                command: dotnet build App.sln
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => DistillConfigLoader.LoadFromYaml(yaml));
        Assert.Contains("at least one check", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadFromYaml_InvalidCommand_Throws()
    {
        const string yaml = """
            version: 1
            profiles:
              quick:
                checks: [build]
            checks:
              build:
                kind: build
                command: ""
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => DistillConfigLoader.LoadFromYaml(yaml));
        Assert.Contains("invalid command", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadFromYaml_BuildKindWithTestVerb_Throws()
    {
        const string yaml = """
            version: 1
            profiles:
              quick:
                checks: [build]
            checks:
              build:
                kind: build
                command: dotnet test App.Tests.csproj
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => DistillConfigLoader.LoadFromYaml(yaml));
        Assert.Contains("kind build but command verb is 'test'", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadFromYaml_TestKindWithBuildVerb_Throws()
    {
        const string yaml = """
            version: 1
            profiles:
              quick:
                checks: [unit]
            checks:
              unit:
                kind: test
                command: dotnet build App.sln
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => DistillConfigLoader.LoadFromYaml(yaml));
        Assert.Contains("kind test but command verb is 'build'", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadFromYaml_GenericKindWithValidDotnetCommand_Succeeds()
    {
        const string yaml = """
            version: 1
            profiles:
              quick:
                checks: [format]
            checks:
              format:
                kind: format
                command: dotnet format --verify-no-changes
            """;

        var config = DistillConfigLoader.LoadFromYaml(yaml);
        Assert.True(config.Checks.ContainsKey("format"));
    }
}
