using System.Text.Json;
using Distill.Testing.Abstractions;

namespace Distill.Testing.MTP;

public sealed class MtpPlatformDetector : ITestPlatformDetector
{
    public async Task<TestPlatformKind> DetectAsync(
        string workspaceRoot,
        string? target,
        CancellationToken cancellationToken,
        string? sourceHint = null)
    {
        if (string.Equals(sourceHint, "vstest", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sourceHint, "vstest-logger", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sourceHint, "trx", StringComparison.OrdinalIgnoreCase))
        {
            return TestPlatformKind.VSTest;
        }

        if (string.Equals(sourceHint, "mtp-report", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sourceHint, "mtp", StringComparison.OrdinalIgnoreCase))
        {
            return TestPlatformKind.Mtp;
        }

        if (TryReadGlobalJsonRunner(workspaceRoot) == TestPlatformKind.Mtp)
        {
            return TestPlatformKind.Mtp;
        }

        if (!string.IsNullOrWhiteSpace(target))
        {
            var projectPath = Path.IsPathRooted(target)
                ? target
                : Path.Combine(workspaceRoot, target);
            if (File.Exists(projectPath)
                && await ProjectUsesMtpAsync(projectPath, cancellationToken).ConfigureAwait(false))
            {
                return TestPlatformKind.Mtp;
            }
        }

        return TestPlatformKind.VSTest;
    }

    private static TestPlatformKind? TryReadGlobalJsonRunner(string workspaceRoot)
    {
        var globalJsonPath = Path.Combine(workspaceRoot, "global.json");
        if (!File.Exists(globalJsonPath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(globalJsonPath));
            if (document.RootElement.TryGetProperty("test", out var testElement)
                && testElement.TryGetProperty("runner", out var runnerElement))
            {
                var runner = runnerElement.GetString();
                if (string.Equals(runner, "Microsoft.Testing.Platform", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(runner, "mtp", StringComparison.OrdinalIgnoreCase))
                {
                    return TestPlatformKind.Mtp;
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static async Task<bool> ProjectUsesMtpAsync(string projectPath, CancellationToken cancellationToken)
    {
        var projectText = await File.ReadAllTextAsync(projectPath, cancellationToken).ConfigureAwait(false);
        return projectText.Contains("<EnableMSTestRunner>true</EnableMSTestRunner>", StringComparison.OrdinalIgnoreCase)
               || projectText.Contains("<EnableNUnitRunner>true</EnableNUnitRunner>", StringComparison.OrdinalIgnoreCase)
               || projectText.Contains("<UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>", StringComparison.OrdinalIgnoreCase)
               || projectText.Contains("Microsoft.Testing.Extensions.TrxReport", StringComparison.OrdinalIgnoreCase);
    }
}
