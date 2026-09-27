using System.Text.Json;
using CodeMap.CSharp.Analysis;

namespace Proof.Adapters.CodeMap;

/// <summary>
/// 커밋별 공개 API 표면을 <c>.proof/surface</c>에 저장한다. P001A가
/// head를 갱신 전 head 인덱스가 아니라 merge-base 리비전과 비교하게 한다.
/// </summary>
internal static class PublicSurfaceSnapshotStore
{
    internal const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    internal static bool IsResolvableRevision(string? revision)
        => !string.IsNullOrWhiteSpace(revision)
           && !revision.Equals("WORKTREE", StringComparison.OrdinalIgnoreCase);

    // 디컴파일된 참조 어셈블리의 project는 external: 로 시작한다.
    // 그 공개 멤버는 저장소 API 표면이 아니다.
    internal static bool IsRepositoryProject(string? projectName)
        => !string.IsNullOrWhiteSpace(projectName)
           && !projectName.StartsWith("external:", StringComparison.Ordinal);

    internal static string SnapshotPath(string workspaceRoot, string commitSha)
        => Path.Combine(workspaceRoot, ".proof", "surface", Sanitize(commitSha) + ".json");

    internal static IReadOnlyDictionary<string, IReadOnlyList<PublicSurfaceEntry>>? TryLoad(
        string workspaceRoot,
        string commitSha)
    {
        var path = SnapshotPath(workspaceRoot, commitSha);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var document = JsonSerializer.Deserialize<PublicSurfaceSnapshotDocument>(File.ReadAllText(path), JsonOptions);
            if (document is null
                || document.SchemaVersion != SchemaVersion
                || !string.Equals(document.CommitSha, commitSha, StringComparison.OrdinalIgnoreCase)
                || document.Projects is null)
            {
                return null;
            }

            var map = new Dictionary<string, IReadOnlyList<PublicSurfaceEntry>>(StringComparer.Ordinal);
            foreach (var project in document.Projects)
            {
                if (!IsRepositoryProject(project.ProjectName) || project.Entries is null)
                {
                    continue;
                }

                map[project.ProjectName] = project.Entries
                    .Select(entry => new PublicSurfaceEntry(
                        entry.Kind ?? string.Empty,
                        entry.QualifiedName ?? string.Empty,
                        entry.Signature ?? string.Empty,
                        entry.Visibility ?? string.Empty,
                        entry.Id))
                    .ToArray();
            }

            return map;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static void TrySave(
        string workspaceRoot,
        string commitSha,
        IReadOnlyDictionary<string, IReadOnlyList<PublicSurfaceEntry>> entriesByProject)
    {
        if (!IsResolvableRevision(commitSha))
        {
            return;
        }

        try
        {
            var path = SnapshotPath(workspaceRoot, commitSha);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var document = new PublicSurfaceSnapshotDocument(
                SchemaVersion,
                commitSha,
                entriesByProject
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new PublicSurfaceProjectSnapshot(
                        pair.Key,
                        pair.Value
                            .OrderBy(entry => entry.Id ?? entry.QualifiedName, StringComparer.Ordinal)
                            .Select(entry => new PublicSurfaceEntryDto(
                                entry.Kind,
                                entry.QualifiedName,
                                entry.Signature,
                                entry.Visibility,
                                entry.Id))
                            .ToArray()))
                    .ToArray());
            File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 스냅샷 쓰기는 캐시 전용이다. 실패는 재사용만 잃는다.
        }
    }

    internal static IReadOnlyDictionary<string, string?> ToFingerprints(
        IReadOnlyDictionary<string, IReadOnlyList<PublicSurfaceEntry>> entriesByProject)
    {
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var pair in entriesByProject)
        {
            map[pair.Key] = pair.Value.Count == 0
                ? null
                : PublicSurfaceFingerprinter.Compute(pair.Value);
        }

        return map;
    }

    private static string Sanitize(string value)
        => new(value.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_').ToArray());

    private sealed record PublicSurfaceSnapshotDocument(
        int SchemaVersion,
        string CommitSha,
        IReadOnlyList<PublicSurfaceProjectSnapshot> Projects);

    private sealed record PublicSurfaceProjectSnapshot(
        string ProjectName,
        IReadOnlyList<PublicSurfaceEntryDto> Entries);

    private sealed record PublicSurfaceEntryDto(
        string? Kind,
        string? QualifiedName,
        string? Signature,
        string? Visibility,
        string? Id);
}
