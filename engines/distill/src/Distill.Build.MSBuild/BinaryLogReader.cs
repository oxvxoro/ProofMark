using Distill.Core.Diagnostics;
using Distill.Core.Evidence;
using Distill.Core.Runs;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;

namespace Distill.Build.MSBuild;

public sealed class BinaryLogReader
{
    public BuildReplayResult Read(string binlogPath, CancellationToken cancellationToken = default)
    {
        var errors = new List<DistillDiagnostic>();
        var warnings = new List<DistillDiagnostic>();
        var builtProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var builtAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var formatVersionMismatch = false;
        var buildSucceeded = true;

        var source = new BinaryLogReplayEventSource
        {
            AllowForwardCompatibility = true
        };

        source.ErrorRaised += (_, e) =>
        {
            errors.Add(BuildEventMapper.MapError(e, errors.Count));
        };

        source.WarningRaised += (_, e) =>
        {
            if (e.Message is not null &&
                (string.Equals(e.Code, "MSBUILDLOGFORMATVERSIONMISMATCH", StringComparison.OrdinalIgnoreCase)
                 || e.Message.Contains("binlog version", StringComparison.OrdinalIgnoreCase)))
            {
                formatVersionMismatch = true;
            }

            warnings.Add(BuildEventMapper.MapWarning(e, warnings.Count));
        };

        source.BuildFinished += (_, e) =>
        {
            buildSucceeded = e.Succeeded;
        };

        source.ProjectFinished += (_, e) =>
        {
            if (!e.Succeeded || string.IsNullOrWhiteSpace(e.ProjectFile))
            {
                return;
            }

            var projectFile = NormalizePath(e.ProjectFile);
            if (projectFile.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                || projectFile.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
                || projectFile.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))
            {
                builtProjects.Add(projectFile);
                builtAssemblies.Add(Path.ChangeExtension(Path.GetFileName(projectFile), ".dll"));
            }
        };

        source.Replay(binlogPath, cancellationToken);

        return new BuildReplayResult(
            errors,
            warnings,
            buildSucceeded,
            formatVersionMismatch,
            builtProjects.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            builtAssemblies.Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string NormalizePath(string path)
        => path.Replace('\\', '/');
}

public sealed record BuildReplayResult(
    IReadOnlyList<DistillDiagnostic> Errors,
    IReadOnlyList<DistillDiagnostic> Warnings,
    bool BuildSucceeded,
    bool FormatVersionMismatch,
    IReadOnlyList<string>? BuiltProjects = null,
    IReadOnlyList<string>? BuiltAssemblies = null);
