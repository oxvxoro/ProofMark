using System.Xml.Linq;

namespace Proof.Tests;

public sealed class ArchitectureDependencyTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Proof.slnx")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName
                   ?? throw new InvalidOperationException("Could not locate Proof.slnx from test output directory.");
        }
    }

    private static IReadOnlyList<string> ProjectReferences(string csprojPath)
    {
        var document = XDocument.Load(csprojPath);
        return document
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => (element.Attribute("Include")?.Value ?? string.Empty).Replace('\\', '/'))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
    }

    private static IEnumerable<string> CsprojFilesUnder(string relativeDirectory)
    {
        var root = Path.Combine(RepoRoot, relativeDirectory);
        return Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories);
    }

    [Fact]
    public void ProofCore_HasNoProjectReferences()
    {
        var path = Path.Combine(RepoRoot, "src", "Proof.Core", "Proof.Core.csproj");
        Assert.Empty(ProjectReferences(path));
    }

    [Fact]
    public void ProofEngine_ReferencesOnlyProofCore()
    {
        var path = Path.Combine(RepoRoot, "src", "Proof.Engine", "Proof.Engine.csproj");
        var refs = ProjectReferences(path);
        Assert.Equal(["../Proof.Core/Proof.Core.csproj"], refs);
    }

    [Fact]
    public void CodeMapStorage_ReferencesOnlyCodeMapCore()
    {
        var path = Path.Combine(RepoRoot, "engines", "codemap", "src", "CodeMap.Storage", "CodeMap.Storage.csproj");
        var refs = ProjectReferences(path);
        Assert.Equal(["../CodeMap.Core/CodeMap.Core.csproj"], refs);
    }

    [Theory]
    [InlineData("engines/codemap/src/CodeMap.CSharp/CodeMap.CSharp.csproj")]
    [InlineData("engines/codemap/src/CodeMap.Web/CodeMap.Web.csproj")]
    [InlineData("engines/codemap/src/CodeMap.Scip/CodeMap.Scip.csproj")]
    public void CodeMapLanguageProjects_DoNotReferenceStorageOrEngine(string relativePath)
    {
        var path = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        foreach (var reference in ProjectReferences(path))
        {
            Assert.DoesNotContain("CodeMap.Storage", reference, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("CodeMap.Engine", reference, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void CodeMapProjects_DoNotReferenceProofOrDistill()
    {
        foreach (var csproj in CsprojFilesUnder(Path.Combine("engines", "codemap")))
        {
            foreach (var reference in ProjectReferences(csproj))
            {
                Assert.DoesNotContain("Proof.", reference, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Distill.", reference, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void DistillProjects_DoNotReferenceProofOrCodeMap()
    {
        foreach (var csproj in CsprojFilesUnder(Path.Combine("engines", "distill")))
        {
            foreach (var reference in ProjectReferences(csproj))
            {
                Assert.DoesNotContain("Proof.", reference, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("CodeMap.", reference, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void ProofAdaptersCodeMap_ReferencesOnlyAllowedCodeMapProjects()
    {
        var path = Path.Combine(RepoRoot, "src", "Proof.Adapters.CodeMap", "Proof.Adapters.CodeMap.csproj");
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "../../engines/codemap/src/CodeMap.Engine/CodeMap.Engine.csproj",
            "../../engines/codemap/src/CodeMap.Storage/CodeMap.Storage.csproj",
            "../../engines/codemap/src/CodeMap.CSharp/CodeMap.CSharp.csproj",
            "../Proof.Core/Proof.Core.csproj"
        };

        var refs = ProjectReferences(path);
        Assert.Subset(allowed, refs.ToHashSet(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(allowed, refs.ToHashSet(StringComparer.OrdinalIgnoreCase));
    }
}
