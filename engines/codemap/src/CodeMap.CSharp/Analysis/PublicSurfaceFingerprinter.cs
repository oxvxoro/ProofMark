using System.Security.Cryptography;
using System.Text;
using CodeMap.Core.Models;

namespace CodeMap.CSharp.Analysis;

public sealed record PublicSurfaceEntry(
    string Kind,
    string QualifiedName,
    string Signature,
    string Visibility,
    string? Id = null);

public enum PublicSurfaceChangeKind
{
    Unchanged,
    Additive,
    Breaking,
    Mixed,
    Inconclusive
}

public sealed record PublicSurfaceDiff(
    PublicSurfaceChangeKind Kind,
    IReadOnlyList<PublicSurfaceEntry> Added,
    IReadOnlyList<PublicSurfaceEntry> Removed,
    IReadOnlyList<PublicSurfaceEntry> Modified);

public static class PublicSurfaceFingerprinter
{
    public static IReadOnlyList<PublicSurfaceEntry> Capture(IEnumerable<CodeNode> declarations)
        => declarations
            .Where(node => string.Equals(node.Visibility, "public", StringComparison.OrdinalIgnoreCase))
            .OrderBy(node => node.Id, StringComparer.Ordinal)
            .Select(node => new PublicSurfaceEntry(
                node.Kind.ToString(),
                node.QualifiedName,
                node.Signature ?? string.Empty,
                node.Visibility ?? string.Empty,
                node.Id))
            .ToArray();

    public static string Compute(IEnumerable<CodeNode> declarations)
        => Compute(Capture(declarations));

    public static string Compute(IReadOnlyList<PublicSurfaceEntry> entries)
    {
        var material = string.Join("\n", entries
            .OrderBy(entry => entry.Id ?? entry.QualifiedName, StringComparer.Ordinal)
            .Select(entry => $"{entry.Kind}|{entry.QualifiedName}|{entry.Signature}|{entry.Visibility}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }
}

public static class PublicSurfaceComparer
{
    public static PublicSurfaceDiff Compare(
        IReadOnlyList<PublicSurfaceEntry>? baseline,
        IReadOnlyList<PublicSurfaceEntry>? head)
    {
        if (baseline is null || head is null)
        {
            return new PublicSurfaceDiff(PublicSurfaceChangeKind.Inconclusive, [], [], []);
        }

        // 같은 이름의 오버로드는 시그니처로 구분한다. 이름만 키로 쓰면
        // CreateApplicationBuilder()와 CreateApplicationBuilder(string[])가
        // 한 키로 뭉쳐 사전이 예외를 던진다.
        var baselineGroups = GroupByMemberName(baseline);
        var headGroups = GroupByMemberName(head);
        var added = new List<PublicSurfaceEntry>();
        var removed = new List<PublicSurfaceEntry>();
        var modified = new List<PublicSurfaceEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in head)
        {
            var name = NameKey(entry);
            if (!seen.Add(name))
            {
                continue;
            }

            if (!baselineGroups.TryGetValue(name, out var before))
            {
                added.AddRange(headGroups[name].Values);
                continue;
            }

            DiffMember(before, headGroups[name], added, removed, modified);
        }

        foreach (var entry in baseline)
        {
            var name = NameKey(entry);
            if (!seen.Add(name))
            {
                continue;
            }

            if (!headGroups.ContainsKey(name))
            {
                removed.AddRange(baselineGroups[name].Values);
            }
        }

        var breaking = removed.Count > 0 || modified.Count > 0;
        var additive = added.Count > 0;
        var kind = (breaking, additive) switch
        {
            (false, false) => PublicSurfaceChangeKind.Unchanged,
            (false, true) => PublicSurfaceChangeKind.Additive,
            (true, false) => PublicSurfaceChangeKind.Breaking,
            _ => PublicSurfaceChangeKind.Mixed
        };
        return new PublicSurfaceDiff(kind, added, removed, modified);
    }

    private static string NameKey(PublicSurfaceEntry entry) => $"{entry.Kind}|{entry.QualifiedName}";

    private static Dictionary<string, Dictionary<string, PublicSurfaceEntry>> GroupByMemberName(
        IReadOnlyList<PublicSurfaceEntry> entries)
    {
        var groups = new Dictionary<string, Dictionary<string, PublicSurfaceEntry>>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var name = NameKey(entry);
            if (!groups.TryGetValue(name, out var bySignature))
            {
                bySignature = new Dictionary<string, PublicSurfaceEntry>(StringComparer.Ordinal);
                groups[name] = bySignature;
            }

            // 같은 시그니처가 두 번 잡혀도 공개 멤버는 하나다.
            bySignature.TryAdd(entry.Signature, entry);
        }

        return groups;
    }

    private static void DiffMember(
        Dictionary<string, PublicSurfaceEntry> before,
        Dictionary<string, PublicSurfaceEntry> after,
        List<PublicSurfaceEntry> added,
        List<PublicSurfaceEntry> removed,
        List<PublicSurfaceEntry> modified)
    {
        if (before.Count == 1 && after.Count == 1)
        {
            var previous = before.Values.First();
            var current = after.Values.First();
            if (!string.Equals(previous.Signature, current.Signature, StringComparison.Ordinal))
            {
                modified.Add(current);
            }

            return;
        }

        foreach (var entry in after.Values)
        {
            if (!before.ContainsKey(entry.Signature))
            {
                added.Add(entry);
            }
        }

        foreach (var entry in before.Values)
        {
            if (!after.ContainsKey(entry.Signature))
            {
                removed.Add(entry);
            }
        }
    }
}
