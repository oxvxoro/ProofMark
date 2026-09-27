using CodeMap.Core;
using CodeMap.CSharp;
using CodeMap.Web;

namespace CodeMap.Engine;

public static class CodeMapEngineBootstrap
{
    private static int _initialized;

    public static void EnsureInitialized()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
            return;

        CodeMapAnalyzerVersions.Register(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["csharp"] = CSharpLanguageAnalyzer.AnalyzerVersion,
                ["web"] = WebLanguageAnalyzer.AnalyzerVersion
            });
    }
}
