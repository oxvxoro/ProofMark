using Distill.Git;

namespace Distill.Tests.Git;

public sealed class ChangedFileCollectorParseTests
{
    [Fact]
    public void ParseNameStatusZ_ReadsRenameAndDelete()
    {
        var output = "M\0src/A.cs\0R100\0src/Old.cs\0src/New.cs\0D\0src/Gone.cs\0";
        var changes = ChangedFileCollector.ParseNameStatusZ(output);
        Assert.Contains(changes, item => item.Kind == GitFileChangeKind.Modified && item.NewPath == "src/A.cs");
        Assert.Contains(changes, item => item.Kind == GitFileChangeKind.Renamed && item.OldPath == "src/Old.cs" && item.NewPath == "src/New.cs");
        Assert.Contains(changes, item => item.Kind == GitFileChangeKind.Deleted && item.OldPath == "src/Gone.cs");
    }

    [Fact]
    public void ParseDiffRawZ_DetectsSymlinkAndRename()
    {
        var output = ":100644 100644 abc def M\0src/A.cs\0:100644 120000 abc def T\0src/link\0:100644 100644 abc def R100\0src/Old.bin\0src/New.bin\0";
        var changes = ChangedFileCollector.ParseDiffRawZ(output);
        Assert.Contains(changes, item => item.Kind == GitFileChangeKind.Modified && item.NewPath == "src/A.cs");
        Assert.Contains(changes, item => item.Kind == GitFileChangeKind.Unsupported && item.NewPath == "src/link");
        Assert.Contains(changes, item => item.Kind == GitFileChangeKind.Renamed && item.OldPath == "src/Old.bin" && item.NewPath == "src/New.bin");
    }

    [Fact]
    public void IsBinaryDiff_MatchesRenameHeader()
    {
        const string patch = """
            diff --git a/src/Old.bin b/src/New.bin
            GIT binary patch
            literal 12
            """;
        Assert.True(ChangedFileCollector.IsBinaryDiff(patch, "src/Old.bin", "src/New.bin"));
    }

    [Fact]
    public void IsBinaryDiff_DoesNotTreatOtherFilesAsBinary()
    {
        const string patch = """
            diff --git a/.proof-e2e.pid b/.proof-e2e.pid
            new file mode 100644
            index 0000000..1111111
            Binary files /dev/null and b/.proof-e2e.pid differ
            diff --git a/src/Proof.Engine/Planning/UncertaintyObligationRule.cs b/src/Proof.Engine/Planning/UncertaintyObligationRule.cs
            index 2222222..3333333 100644
            --- a/src/Proof.Engine/Planning/UncertaintyObligationRule.cs
            +++ b/src/Proof.Engine/Planning/UncertaintyObligationRule.cs
            @@ -1,2 +1,2 @@
            -old
            +new
            """;

        Assert.True(ChangedFileCollector.IsBinaryDiff(patch, ".proof-e2e.pid", ".proof-e2e.pid"));
        Assert.False(ChangedFileCollector.IsBinaryDiff(
            patch,
            "src/Proof.Engine/Planning/UncertaintyObligationRule.cs",
            "src/Proof.Engine/Planning/UncertaintyObligationRule.cs"));
    }

    [Fact]
    public void IsBinaryDiff_RequiresTheWholePath()
    {
        const string patch = """
            diff --git a/src/A.csproj b/src/A.csproj
            index 1111111..2222222
            GIT binary patch
            literal 4
            diff --git a/src/A.cs b/src/A.cs
            index 3333333..4444444 100644
            --- a/src/A.cs
            +++ b/src/A.cs
            @@ -1 +1 @@
            -a
            +b
            """;

        Assert.True(ChangedFileCollector.IsBinaryDiff(patch, "src/A.csproj", "src/A.csproj"));
        Assert.False(ChangedFileCollector.IsBinaryDiff(patch, "src/A.cs", "src/A.cs"));
    }

    [Fact]
    public void IsSubmoduleDiff_DoesNotTreatSourceTextAsSubmoduleMode()
    {
        const string patch = """
            diff --git a/src/Collector.cs b/src/Collector.cs
            index 1111111..2222222 100644
            --- a/src/Collector.cs
            +++ b/src/Collector.cs
            @@ -1,2 +1,2 @@
            -return diffPatch.Contains("160000");
            +return diffPatch.Contains("160000", StringComparison.Ordinal);
            diff --git a/vendor/lib b/vendor/lib
            index aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa..bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb 160000
            --- a/vendor/lib
            +++ b/vendor/lib
            @@ -1 +1 @@
            -Subproject commit aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
            +Subproject commit bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
            diff --git a/src/A.cs b/src/A.cs
            index 3333333..4444444 100644
            --- a/src/A.cs
            +++ b/src/A.cs
            @@ -1 +1 @@
            -a
            +b
            """;

        Assert.False(ChangedFileCollector.IsSubmoduleDiff(patch, "src/Collector.cs", "src/Collector.cs"));
        Assert.False(ChangedFileCollector.IsSubmoduleDiff(patch, "src/A.cs", "src/A.cs"));
        Assert.True(ChangedFileCollector.IsSubmoduleDiff(patch, "vendor/lib", "vendor/lib"));
    }
}
