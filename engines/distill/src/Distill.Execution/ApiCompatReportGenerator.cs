using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Distill.Core.Analysis;
using Distill.Core.Runs;
using Distill.Runner;

namespace Distill.Execution;

internal static class ApiCompatReportGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static async Task WriteReportAsync(
        DistillRunContext context,
        string reportPath,
        CancellationToken cancellationToken)
    {
        var baselineDirectory = Environment.GetEnvironmentVariable("PROOF_APICOMPAT_BASELINE");
        if (string.IsNullOrWhiteSpace(baselineDirectory))
        {
            baselineDirectory = Path.Combine(context.WorkspaceRoot, ".proof", "apicompat-baseline");
        }

        var projects = new List<ApiCompatProjectResult>();
        if (!Directory.Exists(baselineDirectory))
        {
            projects.Add(new ApiCompatProjectResult(
                "(repository)",
                "inconclusive",
                $"Baseline directory not found: {baselineDirectory}"));
            await WriteAsync(reportPath, projects, cancellationToken).ConfigureAwait(false);
            return;
        }

        var baselines = Directory.EnumerateFiles(baselineDirectory, "*.nupkg", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (baselines.Length == 0)
        {
            projects.Add(new ApiCompatProjectResult(
                "(repository)",
                "inconclusive",
                "No baseline .nupkg files; pack at the merge base and copy into .proof/apicompat-baseline."));
            await WriteAsync(reportPath, projects, cancellationToken).ConfigureAwait(false);
            return;
        }

        var runner = new ProcessRunner();
        foreach (var baseline in baselines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var packageId = GuessPackageIdFromNupkgFileName(Path.GetFileName(baseline));
            var projectPath = ResolvePackableProject(context.WorkspaceRoot, packageId);
            if (projectPath is null)
            {
                projects.Add(new ApiCompatProjectResult(
                    packageId,
                    "inconclusive",
                    "No packable project matches this baseline package id."));
                continue;
            }

            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            var arguments = new List<string>
            {
                "build",
                projectPath,
                "--no-restore",
                "-v:q",
                "/p:EnablePackageValidation=true",
                $"/p:PackageValidationBaselinePath={baseline.Replace('\\', '/')}",
                "/p:GenerateCompatibilitySuppressionFile=false"
            };

            var result = await runner.RunAsync(
                new ProcessSpec(
                    FileName: "dotnet",
                    Arguments: arguments,
                    WorkingDirectory: context.WorkspaceRoot,
                    Timeout: TimeSpan.FromMinutes(10),
                    StdoutPath: null,
                    StderrPath: null),
                cancellationToken).ConfigureAwait(false);

            if (result.ExitCode == 0)
            {
                projects.Add(new ApiCompatProjectResult(projectName, "additive", null));
                continue;
            }

            var message = result.ErrorMessage;
            if (string.IsNullOrWhiteSpace(message) && result.ExitCode is not null)
            {
                message = $"Package validation failed with exit code {result.ExitCode}.";
            }

            projects.Add(new ApiCompatProjectResult(
                projectName,
                "breaking",
                message ?? "Package validation failed."));
        }

        await WriteAsync(reportPath, projects, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteAsync(
        string reportPath,
        IReadOnlyList<ApiCompatProjectResult> projects,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        var payload = new { projects = projects.Select(item => new { project = item.Project, status = item.Status, message = item.Message }) };
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(payload, JsonOptions), cancellationToken)
            .ConfigureAwait(false);
    }

    private static string? ResolvePackableProject(string workspaceRoot, string packageId)
    {
        string? best = null;
        foreach (var path in Directory.EnumerateFiles(workspaceRoot, "*.csproj", SearchOption.AllDirectories))
        {
            var normalized = path.Replace('\\', '/');
            if (normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryReadPackageId(path, out var id) || !string.Equals(id, packageId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (best is null || string.CompareOrdinal(path, best) < 0)
            {
                best = path;
            }
        }

        return best;
    }

    internal static string GuessPackageIdFromNupkgFileName(string fileName)
    {
        var withoutExtension = Path.GetFileNameWithoutExtension(fileName);
        var parts = withoutExtension.Split('.');
        var cut = parts.Length;
        while (cut > 1 && IsLikelyVersionToken(parts[cut - 1]))
        {
            cut--;
        }

        return string.Join('.', parts.Take(cut));
    }

    private static bool IsLikelyVersionToken(string token)
        => token.All(ch => char.IsDigit(ch) || ch is '.' or '-' or '+')
           || (token.Length > 0 && char.IsDigit(token[0]));

    private static bool TryReadPackageId(string projectPath, out string? packageId)
    {
        packageId = null;
        try
        {
            var document = XDocument.Load(projectPath);
            var propertyGroup = document.Descendants().FirstOrDefault(element =>
                element.Name.LocalName.Equals("PropertyGroup", StringComparison.Ordinal));
            if (propertyGroup is null)
            {
                return false;
            }

            packageId = propertyGroup.Elements()
                .FirstOrDefault(element => element.Name.LocalName.Equals("PackageId", StringComparison.Ordinal))
                ?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(packageId))
            {
                return true;
            }

            packageId = Path.GetFileNameWithoutExtension(projectPath);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (XmlException)
        {
            return false;
        }
    }
}
