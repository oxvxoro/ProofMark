using CodeMap.Core.Models;
using CodeMap.Storage;
using Proof.Adapters.CodeMap;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

public sealed class CodeMapProjectPathMapTests
{
    [Fact]
    public void ToQueryPath_MapsRootSrcTestsAndNestedProjects()
    {
        var projects = new CodeMapProjectPathMap.Project[]
        {
            new("App", ""),
            new("Proof.Core", "src/Proof.Core"),
            new("Proof.Tests", "tests/Proof.Tests"),
            new("CodeMap.Core", "engines/codemap/src/CodeMap.Core")
        };

        Assert.Equal("App/Program.cs", CodeMapProjectPathMap.ToQueryPath("Program.cs", projects));
        Assert.Equal("Proof.Core/Models.cs", CodeMapProjectPathMap.ToQueryPath("src/Proof.Core/Models.cs", projects));
        Assert.Equal(
            "Proof.Tests/Adapters/FooTests.cs",
            CodeMapProjectPathMap.ToQueryPath("tests/Proof.Tests/Adapters/FooTests.cs", projects));
        Assert.Equal(
            "CodeMap.Core/Ids/CodeMapIdGenerator.cs",
            CodeMapProjectPathMap.ToQueryPath("engines/codemap/src/CodeMap.Core/Ids/CodeMapIdGenerator.cs", projects));
    }

    [Fact]
    public void ToQueryPath_UsesLongestDirectoryAndKeepsPathBoundaries()
    {
        var projects = new CodeMapProjectPathMap.Project[]
        {
            new("Lib", "src/Lib"),
            new("Lib.Tests", "src/Lib.Tests"),
            new("App", "")
        };

        Assert.Equal("Lib/A.cs", CodeMapProjectPathMap.ToQueryPath("src/Lib/A.cs", projects));
        Assert.Equal("Lib.Tests/A.cs", CodeMapProjectPathMap.ToQueryPath("src/Lib.Tests/A.cs", projects));
        Assert.Equal("App/README.md", CodeMapProjectPathMap.ToQueryPath("README.md", projects));
    }

    [Fact]
    public void ToQueryPath_LeavesPathsOutsideEveryProject()
    {
        var projects = new CodeMapProjectPathMap.Project[]
        {
            new("Proof.Core", "src/Proof.Core")
        };

        Assert.Equal("proof.yml", CodeMapProjectPathMap.ToQueryPath("proof.yml", projects));
        Assert.Equal("Proof.Core/Models.cs", CodeMapProjectPathMap.ToQueryPath(@"src\Proof.Core\Models.cs", projects));
    }

    [Fact]
    public void ToQueryPath_SharedDirectory_DoesNotSelectEitherProject()
    {
        var projects = new CodeMapProjectPathMap.Project[]
        {
            new("Left", "src/Shared"),
            new("Right", "src/Shared")
        };
        var queryPath = CodeMapProjectPathMap.ToQueryPath("src/Shared/A.cs", projects);
        var service = Service(
            Symbol("left", "Left", "A.cs", "LeftApply"),
            Symbol("right", "Right", "A.cs", "RightApply"));

        Assert.Equal(CodeMapProjectPathMap.AmbiguousOwnerQueryPath, queryPath);
        Assert.Empty(service.SymbolsIntersecting(queryPath, 1, 40));
        Assert.Equal(
            CodeMapProjectPathMap.AmbiguousOwnerQueryPath,
            CodeMapProjectPathMap.ToQueryPath("Program.cs",
            [
                new CodeMapProjectPathMap.Project("App", ""),
                new CodeMapProjectPathMap.Project("Tool", "")
            ]));
    }

    [Fact]
    public void ToQueryPath_MatchesSymbolsStoredRelativeToTheProjectDirectory()
    {
        var projects = new CodeMapProjectPathMap.Project[]
        {
            new("Proof.Engine", "src/Proof.Engine")
        };
        var gitPath = "src/Proof.Engine/Planning/UncertaintyObligationRule.cs";
        var queryPath = CodeMapProjectPathMap.ToQueryPath(gitPath, projects);
        var symbol = new IndexedSymbol(
            "apply",
            "Proof.Engine",
            "file",
            "Planning/UncertaintyObligationRule.cs",
            NodeKind.Method,
            "Apply",
            "UncertaintyObligationRule.Apply",
            null,
            20,
            40,
            "public",
            "csharp");
        var service = new CodeMapQueryService(new CodeMapSnapshot
        {
            Files = [],
            Symbols = [symbol],
            Edges = []
        });

        Assert.Equal("Proof.Engine/Planning/UncertaintyObligationRule.cs", queryPath);
        Assert.Empty(service.SymbolsIntersecting(gitPath, 20, 30));
        Assert.Contains(service.SymbolsIntersecting(queryPath, 20, 30), item => item.Name == "Apply");
    }

    [Fact]
    public void ToQueryPath_RootSrcAndTests_ResolveOnlyTheOwningProject()
    {
        var projects = new CodeMapProjectPathMap.Project[]
        {
            new("App", ""),
            new("ProjectA", "src/ProjectA"),
            new("ProjectA.Tests", "tests/ProjectA.Tests")
        };
        var service = Service(
            Symbol("main", "App", "Program.cs", "Main"),
            Symbol("other", "ProjectA", "Program.cs", "OtherMain"),
            Symbol("src", "ProjectA", "A.cs", "SrcApply"),
            Symbol("tests", "ProjectA.Tests", "A.cs", "TestApply"));

        Assert.Equal("App/Program.cs", CodeMapProjectPathMap.ToQueryPath("Program.cs", projects));
        Assert.Equal("ProjectA/A.cs", CodeMapProjectPathMap.ToQueryPath("src/ProjectA/A.cs", projects));
        Assert.Equal("ProjectA.Tests/A.cs", CodeMapProjectPathMap.ToQueryPath("tests/ProjectA.Tests/A.cs", projects));
        Assert.Equal("ProjectA/A.cs", CodeMapProjectPathMap.ToQueryPath(@"src\ProjectA\A.cs", projects));
        Assert.Equal("ProjectA/A.cs", CodeMapProjectPathMap.ToQueryPath("SRC/ProjectA/A.cs", projects));

        AssertNames(service, "App/Program.cs", "Main");
        AssertNames(service, @"ProjectA\A.cs", "SrcApply");
        AssertNames(service, "projecta/a.cs", "SrcApply");
        AssertNames(service, "ProjectA.Tests/A.cs", "TestApply");
        Assert.Empty(service.SymbolsIntersecting("src/ProjectA/A.cs", 1, 40));
        Assert.DoesNotContain(service.SymbolsIntersecting("App/Program.cs", 1, 40), item => item.Name == "OtherMain");
    }

    [Fact]
    public void ToQueryPath_CaseOnlyOwners_StayUnresolved()
    {
        var projects = new CodeMapProjectPathMap.Project[]
        {
            new("Lib", "src/Lib"),
            new("lib", "src/Lib")
        };
        var service = Service(
            Symbol("upper", "Lib", "A.cs", "Upper"),
            Symbol("lower", "lib", "A.cs", "Lower"));

        var queryPath = CodeMapProjectPathMap.ToQueryPath("src/Lib/A.cs", projects);
        Assert.Equal(CodeMapProjectPathMap.AmbiguousOwnerQueryPath, queryPath);
        Assert.Empty(service.SymbolsIntersecting("Lib/A.cs", 1, 40));
        Assert.Empty(service.SymbolsIntersecting(queryPath, 1, 40));
    }

    [Fact]
    public void UnknownPath_DoesNotResolveSymbols_AndPartialCoverageStaysBlocking()
    {
        var projects = new CodeMapProjectPathMap.Project[]
        {
            new("Proof.Core", "src/Proof.Core")
        };
        var queryPath = CodeMapProjectPathMap.ToQueryPath("proof.yml", projects);
        var service = Service(Symbol("models", "Proof.Core", "Models.cs", "Apply"));

        Assert.Equal("proof.yml", queryPath);
        Assert.Empty(service.SymbolsIntersecting(queryPath, 1, 40));

        var plan = new DeterministicProofPlanner().Plan(new ChangeImpact(
            "main",
            "WORKTREE",
            [new LineSpan("proof.yml", 1, 2)],
            [],
            [],
            [],
            [],
            "partial",
            false,
            Completeness: new ImpactCompleteness(
                CoverageState.Partial,
                CoverageState.Complete,
                2,
                10,
                10,
                false,
                false,
                1,
                0,
                null)));

        Assert.Contains(
            plan.Constraints ?? [],
            constraint => constraint.Code == ProofReasonCodes.ImpactLocationUnknown && constraint.Severity == "blocking");
    }

    [Fact]
    public void ReadProjects_UsesProjectDirectoryForRootSrcAndTests()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-pathmap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var rootProject = Path.Combine(root, "App.csproj");
            var srcProject = Path.Combine(root, "src", "Worker", "Worker.csproj");
            var state = $$"""
                {
                  "projects": [
                    { "projectName": "App", "projectPath": {{Json(rootProject)}} },
                    { "projectName": "Worker", "projectPath": {{Json(srcProject)}} },
                    { "projectName": "Worker.Tests", "projectPath": "tests/Worker.Tests/Worker.Tests.csproj" }
                  ]
                }
                """;

            var projects = CodeMapProjectPathMap.ReadProjects(root, state);

            Assert.Contains(projects, project => project.Name == "App" && project.RepositoryDirectory == "");
            Assert.Contains(projects, project => project.Name == "Worker" && project.RepositoryDirectory == "src/Worker");
            Assert.Contains(projects, project => project.Name == "Worker.Tests" && project.RepositoryDirectory == "tests/Worker.Tests");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertNames(CodeMapQueryService service, string path, params string[] names)
    {
        var actual = service.SymbolsIntersecting(path, 1, 40).Select(symbol => symbol.Name).ToArray();
        Assert.Equal(names, actual);
    }

    private static CodeMapQueryService Service(params IndexedSymbol[] symbols)
        => new(new CodeMapSnapshot
        {
            Files = [],
            Symbols = symbols,
            Edges = []
        });

    private static IndexedSymbol Symbol(string id, string project, string relativePath, string name)
        => new(id, project, "file", relativePath, NodeKind.Method, name, name, null, 10, 20, "public", "csharp");

    private static string Json(string path) => JsonSerializerEscape(path);

    private static string JsonSerializerEscape(string path)
        => System.Text.Json.JsonSerializer.Serialize(path);
}
