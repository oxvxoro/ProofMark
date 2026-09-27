namespace CodeMap.Core;

public static class CodeMapAnalyzerVersions
{
    private static IReadOnlyDictionary<string, string> _current =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, string> Current => _current;

    public static void Register(IReadOnlyDictionary<string, string> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        _current = versions;
    }
}
