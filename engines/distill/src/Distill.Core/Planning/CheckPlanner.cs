using Distill.Core.Config;
using Distill.Core.Runs;

namespace Distill.Core.Planning;

public sealed record PlannedCheck(
    string Id,
    CheckConfig Definition,
    IReadOnlyList<string> DependsOn);

public static class CheckPlanner
{
    public static IReadOnlyList<PlannedCheck> Plan(DistillConfig config, string profileName)
    {
        if (!config.Profiles.TryGetValue(profileName, out var profile))
        {
            throw new KeyNotFoundException($"Profile '{profileName}' was not found in distill.yml.");
        }

        var planned = new List<PlannedCheck>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var checkId in profile.Checks)
        {
            AddCheck(config, checkId, planned, seen);
        }

        return planned;
    }

    private static void AddCheck(
        DistillConfig config,
        string checkId,
        List<PlannedCheck> planned,
        HashSet<string> seen)
    {
        if (!seen.Add(checkId))
        {
            return;
        }

        if (!config.Checks.TryGetValue(checkId, out var definition))
        {
            throw new KeyNotFoundException($"Check '{checkId}' was not found in distill.yml.");
        }

        foreach (var dependency in definition.DependsOn)
        {
            AddCheck(config, dependency, planned, seen);
        }

        planned.Add(new PlannedCheck(checkId, definition, definition.DependsOn));
    }
}
