namespace Distill.Tests.Golden;

internal static class GoldenPaths
{
    public static string RepoRoot => RepoRootLocator.Find();

    public static string GoldenRoot => Path.Combine(RepoRoot, "fixtures", "golden");

    public static string GetExpectedFailurePackPath(string scenarioName)
        => Path.Combine(GoldenRoot, scenarioName, "failure-pack.txt");
}

internal static class RepoRootLocator
{
    public static string Find()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Directory.Packages.props")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test output directory.");
    }
}

internal static class GoldenText
{
    public static string Normalize(string text)
    {
        var normalized = text
            .Replace("\r\n", "\n")
            .Replace('\\', '/');
        return normalized
            .Replace(GoldenPaths.RepoRoot.Replace('\\', '/'), ".")
            .TrimEnd('\n');
    }

    public static void AssertMatchesGolden(string actual, string goldenPath)
    {
        var expected = File.ReadAllText(goldenPath);
        Assert.Equal(Normalize(expected), Normalize(actual));
    }
}
