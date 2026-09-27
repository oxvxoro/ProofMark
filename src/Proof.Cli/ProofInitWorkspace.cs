namespace Proof.Cli;

internal static class ProofInitWorkspace
{
    internal const string PlaceholderSolution = "YourSolution.slnx";

    /// <summary>
    /// 워크스페이스 루트의 주 솔루션 파일 이름을 반환한다(.slnx가 .sln보다 우선).
    /// </summary>
    public static string? FindPrimarySolutionFileName(string workspaceRoot)
    {
        var slnx = Directory.EnumerateFiles(workspaceRoot, "*.slnx")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (slnx is not null)
        {
            return Path.GetFileName(slnx);
        }

        var sln = Directory.EnumerateFiles(workspaceRoot, "*.sln")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return sln is null ? null : Path.GetFileName(sln);
    }

    public static void SubstituteSolutionPlaceholder(string proofYmlPath, string? solutionFileName)
    {
        if (string.IsNullOrWhiteSpace(solutionFileName) || !File.Exists(proofYmlPath))
        {
            return;
        }

        var text = File.ReadAllText(proofYmlPath);
        if (!text.Contains(PlaceholderSolution, StringComparison.Ordinal))
        {
            return;
        }

        text = text.Replace(PlaceholderSolution, solutionFileName, StringComparison.Ordinal);
        File.WriteAllText(proofYmlPath, text);
    }
}
