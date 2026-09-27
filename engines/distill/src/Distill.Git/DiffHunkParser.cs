using System.Text.RegularExpressions;

namespace Distill.Git;

public static partial class DiffHunkParser
{
    [GeneratedRegex(@"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@", RegexOptions.Compiled)]
    private static partial Regex HunkHeaderRegex();

    public static IReadOnlyList<ChangedHunk> Parse(string diffPatch)
    {
        if (string.IsNullOrWhiteSpace(diffPatch))
        {
            return Array.Empty<ChangedHunk>();
        }

        var hunks = new List<ChangedHunk>();
        var lines = diffPatch.Split('\n');
        string? currentFile = null;
        string? lastOldFile = null;
        var hunkLines = new List<string>();
        int oldStart = 0;
        int oldLength = 0;
        int newStart = 0;
        int newLength = 0;
        var inHunk = false;

        void FlushHunk()
        {
            if (currentFile is null || !inHunk)
            {
                return;
            }

            hunks.Add(new ChangedHunk(
                currentFile,
                oldStart,
                oldLength,
                newStart,
                newLength,
                string.Join('\n', hunkLines)));
            hunkLines.Clear();
            inHunk = false;
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("--- a/", StringComparison.Ordinal))
            {
                lastOldFile = line["--- a/".Length..].Replace('\\', '/');
                continue;
            }

            if (line.StartsWith("+++ b/", StringComparison.Ordinal))
            {
                FlushHunk();
                currentFile = line["+++ b/".Length..].Replace('\\', '/');
                continue;
            }

            if (line.StartsWith("+++ /dev/null", StringComparison.Ordinal))
            {
                FlushHunk();
                currentFile = lastOldFile;
                continue;
            }

            if (line.StartsWith("+++ ", StringComparison.Ordinal) && !line.StartsWith("+++ /dev/null", StringComparison.Ordinal))
            {
                FlushHunk();
                currentFile = line[4..].Trim().Replace('\\', '/');
                continue;
            }

            var match = HunkHeaderRegex().Match(line);
            if (match.Success)
            {
                FlushHunk();
                oldStart = int.Parse(match.Groups[1].Value);
                oldLength = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 1;
                newStart = int.Parse(match.Groups[3].Value);
                newLength = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 1;
                hunkLines.Add(line);
                inHunk = true;
                continue;
            }

            if (inHunk)
            {
                hunkLines.Add(line);
            }
        }

        FlushHunk();
        return hunks;
    }

    public static bool ContainsLine(ChangedHunk hunk, int lineNumber)
        => lineNumber >= hunk.NewStart && lineNumber < hunk.NewStart + Math.Max(hunk.NewLength, 1);
}
