using Distill.TestLogger;

namespace Distill.Tests.TestLogger;

public class JsonlEventWriterTests
{
    [Fact]
    public void WriteTestResult_EscapesMultilineAndUnicode()
    {
        var path = Path.Combine(Path.GetTempPath(), "distill-tests", Guid.NewGuid().ToString("N"), "events.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using (var writer = new JsonlEventWriter(path))
        {
            writer.WriteRunStart(DateTimeOffset.Parse("2026-09-05T08:00:00Z"));
            writer.WriteTestResult(
                "OrderTests.CancelTwice",
                "failed",
                12.3,
                "Expected true\r\nbut was false 🚨",
                "at Sample.Tests.OrderTests.CancelTwice() in C:\\repo\\OrderTests.cs:line 42");
            writer.WriteRunComplete(1, 0, 1, 0);
        }

        var lines = File.ReadAllLines(path);
        Assert.Equal(3, lines.Length);
        Assert.Contains("\\n", lines[1]);
        Assert.Contains("🚨", lines[1]);
        Assert.Contains("OrderTests.CancelTwice", lines[1]);
    }

    [Fact]
    public void Escape_HandlesQuotesAndBackslashes()
    {
        Assert.Equal("hello\\\"world\\\\", JsonlEventWriter.Escape("hello\"world\\"));
    }
}
