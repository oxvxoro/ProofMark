using Distill.Core.Config;
using Distill.Core.Planning;
using Distill.Git;
using Distill.Reporting;

namespace Distill.Tests.Core;

public class DistillConfigLoaderTests
{
    [Fact]
    public void LoadFromYaml_ParsesProfilesAndChecks()
    {
        const string yaml = """
            version: 1
            profiles:
              quick:
                checks: [build, unit]
            checks:
              build:
                kind: build
                command: dotnet build App.sln
                source: msbuild-binlog
              unit:
                kind: test
                command: dotnet test tests/Unit/Unit.csproj
                dependsOn: [build]
            """;

        var config = DistillConfigLoader.LoadFromYaml(yaml);

        Assert.Equal(["build", "unit"], config.Profiles["quick"].Checks);
        Assert.Equal("build", config.Checks["build"].Kind);
        Assert.Equal(["build"], config.Checks["unit"].DependsOn);
    }
}

