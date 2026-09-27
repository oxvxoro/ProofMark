using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;

namespace Proof.Adapters.CodeMap;

internal static class MsBuildTestProjectProbe
{
    private static readonly ConcurrentDictionary<string, bool?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static bool TryEvaluateIsTestProject(string projectPath, out bool isTest)
    {
        isTest = false;
        if (!File.Exists(projectPath))
        {
            return false;
        }

        var cacheKey = $"{projectPath}|{File.GetLastWriteTimeUtc(projectPath).Ticks}";
        if (Cache.TryGetValue(cacheKey, out var cached) && cached is not null)
        {
            isTest = cached.Value;
            return true;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"msbuild \"{projectPath}\" -getProperty:IsTestProject -nologo",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(TimeSpan.FromSeconds(30));
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return false;
            }

            if (string.Equals(output, "true", StringComparison.OrdinalIgnoreCase))
            {
                isTest = true;
                Cache[cacheKey] = true;
                return true;
            }

            if (string.Equals(output, "false", StringComparison.OrdinalIgnoreCase))
            {
                isTest = false;
                Cache[cacheKey] = false;
                return true;
            }

            return false;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }
}
