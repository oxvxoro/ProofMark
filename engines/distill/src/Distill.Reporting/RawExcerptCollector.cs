using Distill.Core.Diagnostics;
using Distill.Core.Planning;

namespace Distill.Reporting;

public static class RawExcerptCollector
{
    public static IReadOnlyList<RawExcerpt> Collect(
        IReadOnlyList<CheckRunResult> checks,
        SufficiencyAssessment assessment,
        int maxLines = 40)
    {
        if (assessment.IsSufficient && !assessment.ForceUncertain)
        {
            return Array.Empty<RawExcerpt>();
        }

        var excerpts = new List<RawExcerpt>();
        foreach (var check in checks)
        {
            if (check.Status == Distill.Core.Runs.VerificationStatus.Pass)
            {
                continue;
            }

            var needsExcerpt = assessment.Notes.Any(note =>
                note.Contains("without", StringComparison.OrdinalIgnoreCase)
                || note.Contains("no build diagnostics", StringComparison.OrdinalIgnoreCase)
                || note.Contains("confidence", StringComparison.OrdinalIgnoreCase));

            if (!needsExcerpt && check.Status is not (Distill.Core.Runs.VerificationStatus.Fail
                or Distill.Core.Runs.VerificationStatus.Uncertain
                or Distill.Core.Runs.VerificationStatus.InfraError))
            {
                continue;
            }

            var paths = CandidatePaths(check);
            foreach (var path in paths)
            {
                if (!File.Exists(path) || IsBinary(path))
                {
                    continue;
                }

                var lines = ReadTail(path, maxLines);
                if (lines.Count == 0)
                {
                    continue;
                }

                excerpts.Add(new RawExcerpt(
                    check.CheckId,
                    path,
                    lines,
                    DetermineReason(check, assessment)));
                break;
            }
        }

        return excerpts;
    }

    private static IEnumerable<string> CandidatePaths(CheckRunResult check)
    {
        if (!string.IsNullOrWhiteSpace(check.ArtifactPointer)
            && File.Exists(check.ArtifactPointer))
        {
            if (!IsBinary(check.ArtifactPointer))
            {
                yield return check.ArtifactPointer;
            }

            var directory = Path.GetDirectoryName(check.ArtifactPointer);
            if (directory is not null)
            {
                foreach (var name in new[]
                {
                    "stderr.log",
                    "logger.stderr.log",
                    "stdout.log",
                    "logger.stdout.log"
                })
                {
                    yield return Path.Combine(directory, name);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(check.ArtifactPointer)
            && Directory.Exists(check.ArtifactPointer))
        {
            foreach (var name in new[]
            {
                "stderr.log",
                "logger.stderr.log",
                "stdout.log",
                "logger.stdout.log",
                "diagnostics.json",
                "tests.events.jsonl",
                "fallback.trx"
            })
            {
                yield return Path.Combine(check.ArtifactPointer, name);
            }
        }
    }

    private static IReadOnlyList<string> ReadTail(string path, int maxLines)
    {
        try
        {
            return File.ReadLines(path).TakeLast(maxLines).ToList();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static bool IsBinary(string path)
        => Path.GetExtension(path).Equals(".binlog", StringComparison.OrdinalIgnoreCase);

    private static string DetermineReason(
        CheckRunResult check,
        SufficiencyAssessment assessment)
    {
        if (assessment.Notes.Any(note =>
                note.Contains("confidence", StringComparison.OrdinalIgnoreCase)))
        {
            return "LOW CONFIDENCE";
        }

        if (check.Status == Distill.Core.Runs.VerificationStatus.InfraError)
        {
            return "INFRASTRUCTURE";
        }

        return "INSUFFICIENT STRUCTURED EVIDENCE";
    }
}
