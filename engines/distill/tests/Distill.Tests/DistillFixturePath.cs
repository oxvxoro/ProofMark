using Distill.Tests.Golden;

namespace Distill.Tests;

internal static class DistillFixturePath
{
    public static string Resolve(params string[] parts)
    {
        var relative = Path.Combine(parts);
        var fromOutput = Path.Combine(new[] { AppContext.BaseDirectory, "fixtures" }.Concat(parts).ToArray());
        if (File.Exists(fromOutput))
        {
            return fromOutput;
        }

        var fromRepo = Path.Combine(GoldenPaths.RepoRoot, "fixtures", relative);
        if (File.Exists(fromRepo))
        {
            return fromRepo;
        }

        throw new FileNotFoundException($"Distill fixture not found: {relative.Replace('\\', '/')}", fromOutput);
    }
}
