using System.Reflection;

namespace CodeMap.Core;

public static class CodeMapToolVersion
{
    public static string Current =>
        typeof(CodeMapToolVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(CodeMapToolVersion).Assembly.GetName().Version?.ToString()
        ?? "unknown";
}
