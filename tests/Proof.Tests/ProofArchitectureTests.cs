using System.Xml.Linq;

namespace Proof.Tests;

public sealed class ProofArchitectureTests
{
    [Fact]
    public void ProofCore_DoesNotReferenceCodeMapOrDistill()
        => AssertNoReference("src/Proof.Core/Proof.Core.csproj", "CodeMap.", "Distill.");

    [Fact]
    public void ProofEngine_DoesNotReferenceAdaptersOrEngines()
        => AssertNoReference("src/Proof.Engine/Proof.Engine.csproj", "CodeMap.", "Distill.", "Proof.Adapters.");

    [Fact]
    public void CodeMapAdapter_DoesNotReferenceDistill()
        => AssertNoReference("src/Proof.Adapters.CodeMap/Proof.Adapters.CodeMap.csproj", "Distill.");

    [Fact]
    public void DistillAdapter_DoesNotReferenceCodeMap()
        => AssertNoReference("src/Proof.Adapters.Distill/Proof.Adapters.Distill.csproj", "CodeMap.");

    [Fact]
    public void CodeMapStorage_DoesNotReferenceLanguageProjects()
        => AssertNoReference(
            "engines/codemap/src/CodeMap.Storage/CodeMap.Storage.csproj",
            "CodeMap.CSharp",
            "CodeMap.Web",
            "CodeMap.Scip");

    private static void AssertNoReference(string relativeProject, params string[] forbidden)
    {
        var path = Path.Combine(RepoRoot(), relativeProject.Replace('/', Path.DirectorySeparatorChar));
        var references = XDocument.Load(path)
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();
        foreach (var name in forbidden)
        {
            Assert.DoesNotContain(references, reference => reference.Contains(name, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Proof.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }
}
