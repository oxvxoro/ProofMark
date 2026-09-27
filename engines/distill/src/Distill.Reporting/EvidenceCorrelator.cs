using Distill.Core.Diagnostics;
using Distill.Git;

namespace Distill.Reporting;

public sealed record RankedDiagnostic(
    DistillDiagnostic Diagnostic,
    int Score,
    IReadOnlyList<string> Reasons);

public static class EvidenceCorrelator
{
    public static IReadOnlyList<RankedDiagnostic> Rank(
        IEnumerable<DistillDiagnostic> diagnostics,
        IEnumerable<ChangedHunk> hunks,
        IEnumerable<string> changedFiles,
        string? workspaceRoot = null)
    {
        var hunkList = hunks.ToList();
        var changedFileSet = changedFiles
            .Select(file => NormalizePath(file, workspaceRoot))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var ranked = diagnostics
            .Select(diagnostic => ScoreDiagnostic(diagnostic, hunkList, changedFileSet, workspaceRoot))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Diagnostic.Severity)
            .ThenBy(item => item.Diagnostic.Id, StringComparer.Ordinal)
            .ToList();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var deduped = new List<RankedDiagnostic>();
        foreach (var item in ranked)
        {
            var normalizedFile = item.Diagnostic.Location is null
                ? null
                : NormalizePath(item.Diagnostic.Location.File, workspaceRoot);
            var key = $"{item.Diagnostic.Kind}|{item.Diagnostic.Code}|{item.Diagnostic.Message}|{item.Diagnostic.TestName}|{normalizedFile}|{item.Diagnostic.Location?.Line}";
            if (seen.Add(key))
            {
                deduped.Add(item);
            }
        }

        return deduped;
    }

    private static RankedDiagnostic ScoreDiagnostic(
        DistillDiagnostic diagnostic,
        IReadOnlyList<ChangedHunk> hunks,
        HashSet<string> changedFiles,
        string? workspaceRoot)
    {
        var score = 0;
        var reasons = new List<string>();

        if (diagnostic.Location is not null)
        {
            var file = NormalizePath(diagnostic.Location.File, workspaceRoot);
            if (changedFiles.Contains(file))
            {
                score += CorrelationScore.DiagnosticInChangedFile;
                reasons.Add("changed-file");
            }

            if (hunks.Any(hunk => string.Equals(NormalizePath(hunk.File, workspaceRoot), file, StringComparison.OrdinalIgnoreCase)
                                  && DiffHunkParser.ContainsLine(hunk, diagnostic.Location.Line)))
            {
                score += CorrelationScore.DiagnosticLineInsideChangedHunk;
                reasons.Add("line-in-hunk");
            }
        }

        foreach (var frame in diagnostic.Frames)
        {
            if (frame.IsFramework)
            {
                score += CorrelationScore.FrameworkOnlyFrame;
                continue;
            }

            if (frame.File is not null)
            {
                var frameFile = NormalizePath(frame.File, workspaceRoot);
                if (changedFiles.Contains(frameFile))
                {
                    score += CorrelationScore.DiagnosticInChangedFile;
                    reasons.Add("frame-changed-file");
                }

                if (frame.Line > 0 && hunks.Any(hunk =>
                        string.Equals(NormalizePath(hunk.File, workspaceRoot), frameFile, StringComparison.OrdinalIgnoreCase)
                        && DiffHunkParser.ContainsLine(hunk, frame.Line)))
                {
                    score += CorrelationScore.StackFrameInsideChangedHunk;
                    reasons.Add("frame-in-hunk");
                }
            }
        }

        if (diagnostic.Kind == DiagnosticKind.Test
            && TestFileIsChanged(diagnostic, changedFiles, workspaceRoot))
        {
            score += CorrelationScore.FailedTestFileChanged;
            reasons.Add("test-file-changed");
        }

        if (diagnostic.Kind == DiagnosticKind.Build && diagnostic.Severity == DiagnosticSeverity.Error)
        {
            score += CorrelationScore.DirectCompilerError;
            reasons.Add("compiler-error");
        }

        if (diagnostic.Kind == DiagnosticKind.Test)
        {
            score += CorrelationScore.FailedAssertion;
            reasons.Add("test-failure");
        }

        if (diagnostic.Severity == DiagnosticSeverity.Warning)
        {
            score += CorrelationScore.Warning;
        }

        if (IsGeneratedFile(diagnostic.Location?.File, workspaceRoot))
        {
            score += CorrelationScore.GeneratedFile;
            reasons.Add("generated-file");
        }

        return new RankedDiagnostic(diagnostic, score, reasons);
    }

    private static bool TestFileIsChanged(
        DistillDiagnostic diagnostic,
        HashSet<string> changedFiles,
        string? workspaceRoot)
    {
        if (diagnostic.Location is not null
            && changedFiles.Contains(NormalizePath(diagnostic.Location.File, workspaceRoot)))
        {
            return true;
        }

        return diagnostic.Frames
            .Where(frame => !frame.IsFramework && frame.File is not null)
            .Select(frame => NormalizePath(frame.File!, workspaceRoot))
            .Any(changedFiles.Contains);
    }

    internal static string NormalizePath(string path, string? workspaceRoot = null)
        => Distill.Core.Paths.DistillPath.ToIdentity(path, workspaceRoot);

    private static bool IsGeneratedFile(string? file, string? workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            return false;
        }

        var normalized = NormalizePath(file, workspaceRoot);
        return normalized.StartsWith("obj/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase);
    }
}
