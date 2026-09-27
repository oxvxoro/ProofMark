namespace Distill.Testing.VSTest;

public static class TestLoggerPathResolver
{
    public const string LoggerAssemblyName = "Distill.TestLogger.dll";

    public static string ResolveExtensionDirectory()
    {
        var probeRoots = new List<string>();

        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            var processDirectory = Path.GetDirectoryName(Environment.ProcessPath);
            if (!string.IsNullOrWhiteSpace(processDirectory))
            {
                probeRoots.Add(processDirectory);
            }
        }

        probeRoots.Add(AppContext.BaseDirectory);

        foreach (var root in probeRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var candidate = Path.Combine(root, LoggerAssemblyName);
            if (File.Exists(candidate))
            {
                return root;
            }
        }

        throw new FileNotFoundException($"{LoggerAssemblyName} was not found. Run `distill doctor` after building the solution.");
    }
}
