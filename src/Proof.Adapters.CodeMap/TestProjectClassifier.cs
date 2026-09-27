using System.Text.Json;
using System.Xml.Linq;
using CodeMap.Core.Models;
using CodeMap.Storage;

namespace Proof.Adapters.CodeMap;

/// <summary>
/// 프로젝트 경로를 알면 <c>.csproj</c> 사실로 테스트 프로젝트를 분류한다.
/// 모르면 프로젝트마다 기존 경로/이름 휴리스틱으로 돌아간다.
/// </summary>
internal sealed class TestProjectClassifier
{
    private readonly Dictionary<string, bool> _byProject = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _heuristicProjects = new(StringComparer.OrdinalIgnoreCase);

    public TestProjectClassifier(string workspaceRoot, string? indexInput)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        foreach (var (projectName, projectPath) in ReadProjectPaths(indexInput ?? workspaceRoot))
        {
            var fullPath = Path.IsPathRooted(projectPath)
                ? projectPath
                : Path.GetFullPath(Path.Combine(workspaceRoot, projectPath));
            if (TryClassifyFromProjectFile(fullPath, out var isTest))
            {
                _byProject[projectName] = isTest;
            }
            else
            {
                _heuristicProjects.Add(projectName);
            }
        }
    }

    public bool IsTestSymbol(IndexedSymbol symbol)
    {
        if (_byProject.TryGetValue(symbol.Project, out var isTestProject))
        {
            return isTestProject;
        }

        if (!_heuristicProjects.Contains(symbol.Project))
        {
            _heuristicProjects.Add(symbol.Project);
        }

        return IsTestSymbolHeuristic(symbol);
    }

    internal static bool IsTestSymbolHeuristic(IndexedSymbol symbol)
    {
        var pathSegments = symbol.RelativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return pathSegments.Any(segment => segment.Equals("test", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("tests", StringComparison.OrdinalIgnoreCase)
            || segment.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase))
            || symbol.Project.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
            || symbol.Name.EndsWith("Tests", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryClassifyFromProjectFile(string projectPath, out bool isTest)
    {
        isTest = false;
        if (!File.Exists(projectPath))
        {
            return false;
        }

        try
        {
            var document = XDocument.Load(projectPath);
            var root = document.Root;
            if (root is null)
            {
                return false;
            }

            foreach (var property in root.Descendants().Where(element => element.Name.LocalName == "IsTestProject"))
            {
                if (string.Equals(property.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase))
                {
                    isTest = true;
                    return true;
                }

                if (string.Equals(property.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase))
                {
                    isTest = false;
                    return true;
                }
            }

            var hasTestSdk = root.Descendants()
                .Where(element => element.Name.LocalName == "PackageReference")
                .Any(element =>
                {
                    var packageReference = element.Attribute("Include")?.Value ?? element.Attribute("Update")?.Value;
                    return packageReference is not null
                           && packageReference.Contains("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase);
                });
            if (hasTestSdk)
            {
                isTest = true;
                return true;
            }

            // 프로젝트 로컬 사실을 찾지 못했다. 값은 Directory.Build.props나
            // 다른 가져온 파일이 공급할 수 있다.
            if (MsBuildTestProjectProbe.TryEvaluateIsTestProject(projectPath, out isTest))
            {
                return true;
            }

            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return false;
        }
    }

    private static IReadOnlyList<(string ProjectName, string ProjectPath)> ReadProjectPaths(string indexInput)
    {
        try
        {
            var database = CodeMapIndexLocator.FindDatabase(indexInput);
            var statePath = Path.Combine(Path.GetDirectoryName(database)!, "state.json");
            if (!File.Exists(statePath))
            {
                return [];
            }

            using var document = JsonDocument.Parse(File.ReadAllText(statePath));
            if (!document.RootElement.TryGetProperty("projects", out var projects))
            {
                return [];
            }

            var results = new List<(string, string)>();
            foreach (var project in projects.EnumerateArray())
            {
                var name = project.TryGetProperty("projectName", out var nameElement) ? nameElement.GetString() : null;
                var path = project.TryGetProperty("projectPath", out var pathElement) ? pathElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                results.Add((name, path));
            }

            return results;
        }
        catch (Exception exception) when (exception is IOException or JsonException or FileNotFoundException or DirectoryNotFoundException or InvalidOperationException)
        {
            return [];
        }
    }
}
