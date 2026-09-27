using CodeMap.Core.Models;
using Proof.Adapters.CodeMap;

namespace Proof.Tests;

public sealed class TestProjectClassifierTests
{
    [Fact]
    public void IsTestSymbol_ClassifiesFromCsproj_WhenStateListsProject()
    {
        var root = CreateWorkspace();
        try
        {
            CreateLibraryProject(root, "Lib");
            CreateTestSdkProject(root, "Tests");
            WriteState(
                root,
                [("Lib", "Lib/Lib.csproj"), ("Tests", "Tests/Tests.csproj")]);

            var classifier = new TestProjectClassifier(root, root);
            var libSymbol = Symbol("lib-1", "HelperTests", "Lib/HelperTests.cs", "Lib");
            var testSymbol = Symbol("test-1", "Case", "Tests/Case.cs", "Tests");

            Assert.False(classifier.IsTestSymbol(libSymbol));
            Assert.True(classifier.IsTestSymbol(testSymbol));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IsTestSymbol_ClassifiesFromCsprojPackageReferenceUpdate()
    {
        var root = CreateWorkspace();
        try
        {
            var projectPath = Path.Combine(root, "Worker", "Worker.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Update="Microsoft.NET.Test.Sdk" Version="17.0.0" />
                  </ItemGroup>
                </Project>
                """);
            WriteState(root, [("Worker", "Worker/Worker.csproj")]);

            var classifier = new TestProjectClassifier(root, root);

            Assert.True(classifier.IsTestSymbol(Symbol("worker-1", "Worker", "Worker/Worker.cs", "Worker")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IsTestSymbol_FallsBackToHeuristic_WhenProjectUnknown()
    {
        var classifier = new TestProjectClassifier(Path.GetTempPath(), Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N")));
        var symbol = Symbol("id", "Case", "tests/Case.cs", "Unknown");
        Assert.True(classifier.IsTestSymbol(symbol));
    }

    [Fact]
    public void IsTestSymbol_FallsBackToHeuristic_WhenProjectUsesImportedPropsOnly()
    {
        var root = CreateWorkspace();
        try
        {
            var projectPath = Path.Combine(root, "Lib", "Lib.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="../Directory.Build.props" />
                </Project>
                """);
            File.WriteAllText(Path.Combine(root, "Directory.Build.props"), """
                <Project>
                  <PropertyGroup>
                    <IsTestProject>true</IsTestProject>
                  </PropertyGroup>
                </Project>
                """);
            WriteState(root, [("Lib", "Lib/Lib.csproj")]);

            var classifier = new TestProjectClassifier(root, root);
            Assert.True(classifier.IsTestSymbol(Symbol("id", "HelperTests", "Lib/HelperTests.cs", "Lib")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-testproj-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".codemap"));
        File.WriteAllText(Path.Combine(root, ".codemap", "index.db"), "db");
        return root;
    }

    private static void WriteState(string root, IReadOnlyList<(string ProjectName, string ProjectPath)> projects)
    {
        var entries = string.Join(
            ",",
            projects.Select(project => $$"""
                    {
                      "projectName": "{{project.ProjectName}}",
                      "projectPath": "{{project.ProjectPath.Replace("\\", "\\\\")}}"
                    }
                """));
        var state = $$"""
            {
              "projects": [
            {{entries}}
              ]
            }
            """;
        File.WriteAllText(Path.Combine(root, ".codemap", "state.json"), state);
    }

    private static string CreateLibraryProject(string root, string name)
    {
        var path = Path.Combine(root, name, name + ".csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <IsTestProject>false</IsTestProject>
              </PropertyGroup>
            </Project>
            """);
        return path;
    }

    private static string CreateTestSdkProject(string root, string name)
    {
        var path = Path.Combine(root, name, name + ".csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.0.0" />
              </ItemGroup>
            </Project>
            """);
        return path;
    }

    private static IndexedSymbol Symbol(string id, string name, string relativePath, string project) =>
        new(id, project, "file-1", relativePath, NodeKind.Method, name, $"{project}.{name}", null, 1, 10, "public", "csharp");
}
