using Distill.Testing.VSTest;

namespace Distill.Tests.VSTest;

public class JsonlTestResultParserTests
{
    private static string GetFixturePath(string fileName)
        => DistillFixturePath.Resolve("vstest", fileName);

    [Fact]
    public void Parse_CompleteStream_ReturnsCountsAndDiagnostics()
    {
        var parser = new JsonlTestResultParser();
        var result = parser.Parse(GetFixturePath("failed.events.jsonl"));

        Assert.True(result.IsComplete);
        Assert.NotNull(result.Evidence);
        Assert.Equal(2, result.Evidence!.Passed);
        Assert.Equal(1, result.Evidence.Failed);
        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public void Parse_IncompleteStream_IsNotComplete()
    {
        var parser = new JsonlTestResultParser();
        var result = parser.Parse(GetFixturePath("incomplete.events.jsonl"));

        Assert.False(result.IsComplete);
        Assert.Null(result.Evidence);
        Assert.NotNull(result.ErrorMessage);
    }
}
