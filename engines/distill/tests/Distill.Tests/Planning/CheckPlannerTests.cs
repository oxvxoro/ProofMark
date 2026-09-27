using Distill.Core.Config;
using Distill.Core.Planning;
using Distill.Git;
using Distill.Reporting;

namespace Distill.Tests.Planning;

public class CheckPlannerTests
{
    [Fact]
    public void Plan_OrdersDependenciesBeforeDependents()
    {
        var config = new DistillConfig
        {
            Profiles =
            {
                ["quick"] = new ProfileDefinition { Checks = ["unit"] }
            },
            Checks =
            {
                ["build"] = new CheckConfig
                {
                    Kind = "build",
                    Command = "dotnet build App.sln"
                },
                ["unit"] = new CheckConfig
                {
                    Kind = "test",
                    Command = "dotnet test tests/Unit/Unit.csproj",
                    DependsOn = ["build"]
                }
            }
        };

        var planned = CheckPlanner.Plan(config, "quick");

        Assert.Equal(["build", "unit"], planned.Select(check => check.Id));
    }
}
