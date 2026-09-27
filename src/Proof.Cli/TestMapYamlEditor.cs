using Proof.Core;

namespace Proof.Cli;

/// <summary>
/// <c>policy.testMaps</c>에 대한 주석 보존 문자열 수술. 도그푸드
/// proof.yml은 주석이 빽빽하므로, 이 편집기는 문서 전체를
/// 결코 다시 직렬화하지 않는다. <c>policy:</c> 블록을 찾고, 기존
/// 항목의 <c>tests:</c> 목록에 합치거나 새 항목을 덧붙이고, 나머지
/// 바이트는 그대로 둔다. 순수 텍스트와 검증뿐이다. 엔진 안에는 결코 없다.
/// </summary>
internal static class TestMapYamlEditor
{
    public static string Upsert(string yaml, string symbol, IReadOnlyList<string> tests)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ProofConfigException("policy.testMaps entries must declare a non-empty symbol.");
        }

        if (tests is not { Count: > 0 } || tests.Any(test => string.IsNullOrWhiteSpace(test)))
        {
            throw new ProofConfigException($"policy.testMaps for '{symbol}' must list at least one non-empty test.");
        }

        var newline = yaml.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = SplitLines(yaml).ToList();

        var policyIndex = FindTopLevelKey(lines, "policy");
        if (policyIndex < 0)
        {
            return AppendPolicyBlock(lines, symbol, tests, newline);
        }

        var policyIndent = IndentOf(lines[policyIndex]);
        var blockEnd = BlockEnd(lines, policyIndex);
        var testMapsIndex = FindKey(lines, policyIndex + 1, blockEnd, "testMaps");
        if (testMapsIndex < 0)
        {
            var insertAt = LastKeyLine(lines, policyIndex + 1, blockEnd);
            var indent = policyIndent + 2;
            var insertLines = new List<string> { $"{Spaces(indent)}testMaps:" };
            insertLines.AddRange(RenderEntryLines(indent + 2, symbol, tests));
            lines.InsertRange(insertAt, insertLines);
            return Join(lines, newline);
        }

        var testMapsIndent = IndentOf(lines[testMapsIndex]);
        var testMapsEnd = BlockEnd(lines, testMapsIndex);
        var entries = EntryRanges(lines, testMapsIndex + 1, testMapsEnd, testMapsIndent);
        foreach (var (start, end) in entries)
        {
            var existing = ReadSymbol(lines, start, end);
            if (existing is not null
                && (string.Equals(existing, symbol, StringComparison.OrdinalIgnoreCase)
                    || TestMapMatching.SymbolMatches(symbol, symbol, existing)))
            {
                return UnionTests(lines, newline, start, end, symbol, tests);
            }
        }

        var entryIndent = entries.Length > 0 ? IndentOf(lines[entries[0].Start]) : testMapsIndent + 2;
        var newEntry = RenderEntryLines(entryIndent, symbol, tests);
        var appendAt = entries.Length > 0 ? entries[^1].End : testMapsEnd;
        lines.InsertRange(appendAt, newEntry);
        return Join(lines, newline);
    }

    /// <summary>항목 하나의 YAML 조각만 렌더한다(dry-run 출력).</summary>
    public static string RenderEntry(int indent, string symbol, IReadOnlyList<string> tests)
        => string.Join(Environment.NewLine, RenderEntryLines(indent, symbol, tests));

    private static List<string> RenderEntryLines(int indent, string symbol, IReadOnlyList<string> tests)
    {
        var pad = Spaces(indent);
        var lines = new List<string> { $"{pad}- symbol: \"{symbol}\"" };
        if (tests.Count == 0)
        {
            lines.Add($"{pad}  tests: []");
            return lines;
        }

        lines.Add($"{pad}  tests:");
        foreach (var test in tests)
        {
            lines.Add($"{pad}    - {Quote(test)}");
        }

        return lines;
    }

    private static string UnionTests(
        List<string> lines,
        string newline,
        int entryStart,
        int entryEnd,
        string symbol,
        IReadOnlyList<string> tests)
    {
        var testsIndex = -1;
        var entryIndent = IndentOf(lines[entryStart]);
        for (var index = entryStart + 1; index < entryEnd; index++)
        {
            if (Trimmed(lines[index]).StartsWith("tests:", StringComparison.Ordinal)
                && IndentOf(lines[index]) == entryIndent + 2)
            {
                testsIndex = index;
                break;
            }
        }

        if (testsIndex < 0)
        {
            lines.Insert(entryEnd, $"{Spaces(entryIndent + 2)}tests:");
            testsIndex = entryEnd;
            entryEnd++;
        }

        var inlineEmpty = Trimmed(lines[testsIndex]).Equals("tests: []", StringComparison.Ordinal);
        if (inlineEmpty)
        {
            var indent = IndentOf(lines[testsIndex]) + 2;
            var items = tests.Select(test => $"{Spaces(indent)}- {Quote(test)}").ToList();
            lines.RemoveAt(testsIndex);
            lines.Insert(testsIndex, $"{Spaces(indent - 2)}tests:");
            lines.InsertRange(testsIndex + 1, items);
            return Join(lines, newline);
        }

        var testsIndent = IndentOf(lines[testsIndex]);
        var itemIndent = -1;
        var lastItem = testsIndex;
        var index2 = testsIndex + 1;
        while (index2 < entryEnd)
        {
            var line = lines[index2];
            if (string.IsNullOrWhiteSpace(line))
            {
                index2++;
                continue;
            }

            var indent = IndentOf(line);
            if (indent <= testsIndent)
            {
                break;
            }

            if (Trimmed(line).StartsWith("- ", StringComparison.Ordinal))
            {
                if (itemIndent < 0)
                {
                    itemIndent = indent;
                }

                lastItem = index2;
            }

            index2++;
        }

        if (itemIndent < 0)
        {
            itemIndent = testsIndent + 2;
        }

        var existingItems = new HashSet<string>(StringComparer.Ordinal);
        for (var scan = testsIndex + 1; scan <= lastItem; scan++)
        {
            var value = ReadListItem(lines[scan], itemIndent);
            if (value is not null)
            {
                existingItems.Add(value);
            }
        }

        var additions = tests
            .Where(test => !existingItems.Contains(test) && !existingItems.Contains(Quote(test)))
            .Select(test => $"{Spaces(itemIndent)}- {Quote(test)}")
            .ToList();
        if (additions.Count == 0)
        {
            return Join(lines, newline);
        }

        lines.InsertRange(lastItem + 1, additions);
        return Join(lines, newline);
    }

    private static string AppendPolicyBlock(List<string> lines, string symbol, IReadOnlyList<string> tests, string newline)
    {
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        lines.Add("policy:");
        lines.Add("  testMaps:");
        lines.AddRange(RenderEntryLines(4, symbol, tests));
        return Join(lines, newline);
    }

    private static (int Start, int End)[] EntryRanges(List<string> lines, int start, int end, int testMapsIndent)
    {
        var ranges = new List<(int Start, int End)>();
        var entryIndent = -1;
        for (var index = start; index < end; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                continue;
            }

            var indent = IndentOf(lines[index]);
            if (indent <= testMapsIndent)
            {
                continue;
            }

            // 항목 레벨의 대시 줄만 새 항목을 시작한다. 중첩된
            // 목록 항목(심볼의 tests)은 더 깊고, 현재
            // 항목 블록 안에 남아 있어야 한다.
            if (Trimmed(lines[index]).StartsWith("- ", StringComparison.Ordinal)
                && (entryIndent < 0 || indent == entryIndent))
            {
                if (entryIndent < 0)
                {
                    entryIndent = indent;
                }

                ranges.Add((index, index));
            }
        }

        for (var index = 0; index < ranges.Count; index++)
        {
            var nextStart = index + 1 < ranges.Count ? ranges[index + 1].Start : end;
            ranges[index] = (ranges[index].Start, nextStart);
        }

        return [.. ranges];
    }

    private static string? ReadSymbol(List<string> lines, int entryStart, int entryEnd)
    {
        for (var index = entryStart; index < entryEnd; index++)
        {
            var trimmed = Trimmed(lines[index]);
            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                trimmed = trimmed[2..].TrimStart();
            }

            if (trimmed.StartsWith("symbol:", StringComparison.Ordinal))
            {
                return Unquote(trimmed["symbol:".Length..].Trim());
            }
        }

        return null;
    }

    private static string? ReadListItem(string line, int itemIndent)
    {
        if (IndentOf(line) != itemIndent)
        {
            return null;
        }

        var trimmed = Trimmed(line);
        return trimmed.StartsWith("- ", StringComparison.Ordinal) ? Unquote(trimmed[2..].Trim()) : null;
    }

    private static int FindTopLevelKey(List<string> lines, string key)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line) || IndentOf(line) != 0)
            {
                continue;
            }

            if (Trimmed(line).Equals(key + ":", StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindKey(List<string> lines, int start, int end, string key)
    {
        for (var index = start; index < end; index++)
        {
            if (Trimmed(lines[index]).Equals(key + ":", StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    // 블록의 마지막 비어 있지 않은 줄 뒤에 삽입한다(블록 끝 앞).
    private static int LastKeyLine(List<string> lines, int start, int end)
    {
        for (var index = end - 1; index >= start; index--)
        {
            if (!string.IsNullOrWhiteSpace(lines[index]))
            {
                return index + 1;
            }
        }

        return end;
    }

    private static int BlockEnd(List<string> lines, int keyIndex)
    {
        var keyIndent = IndentOf(lines[keyIndex]);
        var index = keyIndex + 1;
        while (index < lines.Count)
        {
            var line = lines[index];
            if (!string.IsNullOrWhiteSpace(line) && IndentOf(line) <= keyIndent)
            {
                break;
            }

            index++;
        }

        return index;
    }

    private static int IndentOf(string line)
    {
        var count = 0;
        foreach (var character in line)
        {
            if (character == ' ')
            {
                count++;
            }
            else
            {
                break;
            }
        }

        return count;
    }

    private static string Trimmed(string line) => line.TrimStart();

    private static string Spaces(int count) => new(' ', count);

    private static string Quote(string value)
        => value.Contains(": ", StringComparison.Ordinal) || value.StartsWith("#", StringComparison.Ordinal)
            ? $"\"{value}\""
            : value;

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed.StartsWith('"') && trimmed.EndsWith('"'))
        {
            return trimmed[1..^1];
        }

        return trimmed;
    }

    private static IEnumerable<string> SplitLines(string yaml)
        => yaml.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

    private static string Join(List<string> lines, string newline) => string.Join(newline, lines) + newline;

    // 테스트에 노출된다. 출력은 도그푸드 파일처럼
    // 항상 끝 개행으로 끝난다.
    internal static string NormalizeTrailingNewline(string yaml)
        => yaml.Replace("\r\n", "\n").TrimEnd('\n') + Environment.NewLine;
}
