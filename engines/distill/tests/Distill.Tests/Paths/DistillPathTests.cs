using Distill.Core.Paths;

namespace Distill.Tests.Paths;

public class DistillPathTests
{
    [Theory]
    [InlineData(@"C:\runs\unit", "C:/runs/unit")]
    [InlineData(@"C:\runs\unit\tests.events.jsonl", "C:/runs/unit/tests.events.jsonl")]
    [InlineData("/tmp/runs/unit", "/tmp/runs/unit")]
    public void ForCommandArgument_DoesNotResolveVirtualAbsolutePathsThroughHost(string input, string expected)
    {
        Assert.Equal(expected, DistillPath.ForCommandArgument(input));
    }

    [Fact]
    public void ToIdentity_MapsWindowsAbsoluteDiagnosticOntoRelativeGitPath()
    {
        Assert.Equal("src/Foo.cs", DistillPath.ToIdentity(@"C:\repo\src\Foo.cs", @"C:\repo"));
        Assert.Equal("src/Foo.cs", DistillPath.ToIdentity("src/Foo.cs", @"C:\repo"));
        Assert.True(DistillPath.Equivalent(@"C:\repo\src\Foo.cs", "src/Foo.cs", @"C:\repo"));
    }

    [Fact]
    public void ToIdentity_KeepsOutsideWorkspaceAbsolutePathDistinct()
    {
        Assert.Equal("/outside/src/Foo.cs", DistillPath.ToIdentity("/outside/src/Foo.cs", "/repo"));
        Assert.False(DistillPath.Equivalent("/outside/src/Foo.cs", "outside/src/Foo.cs", "/repo"));
    }

    [Fact]
    public void ToIdentity_MapsWindowsAbsoluteDiagnosticOntoPosixWorkspace()
    {
        Assert.Equal("SampleTests.cs", DistillPath.ToIdentity("C:/repo/SampleTests.cs", "/repo"));
        Assert.True(DistillPath.Equivalent("C:/repo/SampleTests.cs", "SampleTests.cs", "/repo"));
    }
}
