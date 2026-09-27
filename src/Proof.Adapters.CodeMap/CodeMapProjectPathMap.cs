using System.Text.Json;
using CodeMap.Storage;

namespace Proof.Adapters.CodeMap;

/// <summary>
/// Git 경로는 저장소 루트 기준이고, CodeMap은 csproj 디렉터리 기준으로 파일을 저장한다.
/// 소유가 하나일 때만 조회 키는 {프로젝트 이름}/{프로젝트 상대 경로}다.
/// 소유 프로젝트는 저장소 루트, src, tests, 그 밖 중첩 디렉터리 중
/// 경로 경계가 맞는 가장 긴 디렉터리로 고른다.
/// 디렉터리 비교는 대소문자를 가리지 않는다. 같은 길이의 소유가 둘이면 맞추지 않는다.
/// </summary>
internal static class CodeMapProjectPathMap
{
    /// <summary>소유를 하나로 정할 수 없을 때 쓰는 조회 키. 인덱스 경로와 같지 않다.</summary>
    internal const string AmbiguousOwnerQueryPath = "\0";

    internal readonly record struct Project(string Name, string RepositoryDirectory);

    internal static string ToQueryPath(string repositoryRelativePath, IReadOnlyList<Project> projects)
    {
        var path = repositoryRelativePath.Replace('\\', '/').TrimStart('/');
        if (path.Length == 0 || projects.Count == 0)
        {
            return path;
        }

        var winner = -1;
        var winnerLength = -1;
        var tie = false;
        for (var index = 0; index < projects.Count; index++)
        {
            var directory = NormalizeDirectory(projects[index].RepositoryDirectory);
            if (!IsInside(path, directory))
            {
                continue;
            }

            if (directory.Length > winnerLength)
            {
                winner = index;
                winnerLength = directory.Length;
                tie = false;
                continue;
            }

            if (directory.Length == winnerLength
                && winner >= 0
                && !string.Equals(projects[winner].Name, projects[index].Name, StringComparison.Ordinal))
            {
                tie = true;
            }
        }

        if (winner < 0)
        {
            return path;
        }

        var owner = projects[winner];
        var relative = RelativeTo(path, NormalizeDirectory(owner.RepositoryDirectory));
        if (relative.Length == 0 || tie || string.IsNullOrEmpty(owner.Name))
        {
            return AmbiguousOwnerQueryPath;
        }

        return owner.Name + "/" + relative;
    }

    internal static IReadOnlyList<Project> Load(string workspaceRoot)
    {
        try
        {
            var database = CodeMapIndexLocator.FindDatabase(workspaceRoot);
            var statePath = Path.Combine(Path.GetDirectoryName(database)!, "state.json");
            if (!File.Exists(statePath))
            {
                return [];
            }

            return ReadProjects(workspaceRoot, File.ReadAllText(statePath));
        }
        catch (Exception exception) when (exception is IOException
            or FileNotFoundException
            or DirectoryNotFoundException
            or JsonException
            or UnauthorizedAccessException)
        {
            return [];
        }
    }

    internal static IReadOnlyList<Project> ReadProjects(string workspaceRoot, string stateJson)
    {
        using var document = JsonDocument.Parse(stateJson);
        if (!document.RootElement.TryGetProperty("projects", out var projects) || projects.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var workspaceFull = Path.GetFullPath(workspaceRoot);
        var results = new List<Project>();
        foreach (var project in projects.EnumerateArray())
        {
            var name = project.TryGetProperty("projectName", out var nameElement) ? nameElement.GetString() : null;
            var projectPath = project.TryGetProperty("projectPath", out var pathElement) ? pathElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(projectPath))
            {
                continue;
            }

            if (TryRepositoryDirectory(workspaceFull, projectPath, out var directory))
            {
                results.Add(new Project(name, directory));
            }
        }

        return results;
    }

    private static bool TryRepositoryDirectory(string workspaceFull, string projectPath, out string directory)
    {
        var full = Path.IsPathRooted(projectPath)
            ? Path.GetFullPath(projectPath)
            : Path.GetFullPath(Path.Combine(workspaceFull, projectPath));
        var projectDirectory = Directory.Exists(full) && !File.Exists(full)
            ? full
            : Path.GetDirectoryName(full) ?? full;
        var relative = Path.GetRelativePath(workspaceFull, projectDirectory);
        if (relative == ".")
        {
            directory = string.Empty;
            return true;
        }

        directory = relative.Replace('\\', '/');
        return directory != ".." && !directory.StartsWith("../", StringComparison.Ordinal);
    }

    private static string NormalizeDirectory(string directory)
        => directory.Replace('\\', '/').Trim('/');

    private static bool IsInside(string path, string directory)
    {
        if (directory.Length == 0)
        {
            return true;
        }

        return string.Equals(path, directory, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(directory + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string RelativeTo(string path, string directory)
    {
        if (directory.Length == 0)
        {
            return path;
        }

        if (string.Equals(path, directory, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return path[(directory.Length + 1)..];
    }
}
