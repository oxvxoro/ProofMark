using Distill.Core.Config;
using Distill.Core.Planning;
using Proof.Core;

namespace Proof.Adapters.Distill;

public sealed class DistillCapabilitySource(string? distillConfigPath = null) : IEvidenceCapabilitySource
{
    public IReadOnlyList<EvidenceCapability> GetCapabilities(CapabilityContext context)
        => BuildCatalog(context.WorkspaceRoot, distillConfigPath, context.Profile);

    public static IReadOnlyList<EvidenceCapability> BuildCatalog(
        string workspaceRoot,
        string? distillConfigPath = null,
        string? profile = null)
    {
        var configPath = ResolveConfigPath(workspaceRoot, distillConfigPath);
        if (!File.Exists(configPath))
        {
            return [];
        }

        var config = DistillConfigLoader.Load(configPath);
        IEnumerable<KeyValuePair<string, CheckConfig>> checks;
        if (string.IsNullOrWhiteSpace(profile))
        {
            checks = config.Checks;
        }
        else
        {
            try
            {
                checks = CheckPlanner.Plan(config, profile)
                    .Select(item => new KeyValuePair<string, CheckConfig>(item.Id, item.Definition));
            }
            catch (KeyNotFoundException)
            {
                return [];
            }
        }

        return checks.Select(pair =>
        {
            if (string.Equals(pair.Value.Kind, "process", StringComparison.OrdinalIgnoreCase))
            {
                return DescribeProcess(workspaceRoot, pair.Key, pair.Value);
            }

            string? target = null;
            try
            {
                target = DotnetCommandParser.Parse(pair.Value.Command).Target;
            }
            catch (ArgumentException)
            {
                target = null;
            }

            var mode = DistillEvidenceMapper.ResolveScopeMode(target);
            return new EvidenceCapability(
                pair.Key,
                pair.Value.Kind,
                target,
                mode,
                pair.Value.DependsOn,
                CostFor(pair.Value.Kind));
        }).ToArray();
    }

    // process 명령은 dotnet 명령이 아니므로 대상을 파싱하지 않는다. source: junit이고
    // project가 해석되는 scip:{name}이면 그 프로젝트의 테스트 케이스를 정확히 낸다고 알린다.
    // 그 밖의 process 검사는 종료 코드뿐이라 어떤 의무도 직접 덮지 않는다.
    private static EvidenceCapability DescribeProcess(string workspaceRoot, string checkId, CheckConfig check)
    {
        var project = check.Project?.Trim();
        if (string.Equals(check.Source, "junit", StringComparison.OrdinalIgnoreCase)
            && project is not null
            && project.StartsWith("scip:", StringComparison.OrdinalIgnoreCase)
            && DistillCommandRewriter.ResolveProjectPath(workspaceRoot, project) is not null)
        {
            return new EvidenceCapability(checkId, "test", project, ScopeMode.Exact, check.DependsOn, CostFor("test"));
        }

        return new EvidenceCapability(checkId, check.Kind, null, ScopeMode.RepositoryWide, check.DependsOn, CostFor(check.Kind));
    }

    public static string ResolveConfigPath(string workspaceRoot, string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(workspaceRoot, configuredPath);
        }

        var direct = Path.Combine(workspaceRoot, "distill.yml");
        if (File.Exists(direct))
        {
            return direct;
        }

        var dotDistill = Path.Combine(workspaceRoot, ".distill", "distill.yml");
        if (File.Exists(dotDistill))
        {
            return dotDistill;
        }

        return direct;
    }

    private static int CostFor(string kind)
        => kind.ToLowerInvariant() switch
        {
            "test" => 3,
            "build" => 2,
            "analysis" => 2,
            // producer-only 검사는 Distill 명령을 실행하지 않으므로,
            // 그리디 set-cover에서 실제 검사보다 높은 입찰을 해서는 안 된다.
            "runtimecoverage" or "runtime-coverage" or "coverage" => 1,
            _ => 1
        };
}
