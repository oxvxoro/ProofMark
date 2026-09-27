using Distill.Core.Diagnostics;
using Distill.Core.Runs;
using Distill.Reporting;

namespace Distill.Tests.Reliability;

public class ReliabilityTests
{
    [Fact]
    public void FormatDiagnosticParser_MapsKnownDotnetFormatOutput()
    {
        var diagnostics = FormatDiagnosticParser.Parse(
        [
            "Would format src/Program.cs(12,4)",
            "  1 file would be formatted."
        ]);

        Assert.NotEmpty(diagnostics);
        Assert.Equal(DiagnosticKind.Format, diagnostics[0].Kind);
        Assert.Equal("src/Program.cs", diagnostics[0].Location?.File);
        Assert.Equal(12, diagnostics[0].Location?.Line);
        Assert.Equal(DiagnosticProvenance.KnownTextParser, diagnostics[0].Provenance);
    }

    [Fact]
    public void RunRetention_KeepsRecentRunsAndDeletesOldRuns()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"distill-retention-{Guid.NewGuid():N}");
        try
        {
            var runsRoot = RunArtifactLayout.GetRunsRoot(workspace);
            Directory.CreateDirectory(runsRoot);
            for (var index = 0; index < 22; index++)
            {
                var directory = Directory.CreateDirectory(
                    Path.Combine(runsRoot, $"d-{index:000}"));
                directory.LastWriteTimeUtc = DateTime.UtcNow.AddDays(-index);
            }

            RunRetention.Prune(workspace, keepLast: 20, maxAgeDays: 7);

            Assert.True(Directory.Exists(Path.Combine(runsRoot, "d-000")));
            Assert.True(Directory.Exists(Path.Combine(runsRoot, "d-019")));
            Assert.False(Directory.Exists(Path.Combine(runsRoot, "d-020")));
            Assert.False(Directory.Exists(Path.Combine(runsRoot, "d-021")));
        }
        finally
        {
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }
}
