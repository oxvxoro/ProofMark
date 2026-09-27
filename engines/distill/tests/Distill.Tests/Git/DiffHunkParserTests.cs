using Distill.Core.Config;
using Distill.Core.Planning;
using Distill.Git;
using Distill.Reporting;

namespace Distill.Tests.Git;

public class DiffHunkParserTests
{
    [Fact]
    public void Parse_ExtractsSingleHunk()
    {
        const string patch = """
            diff --git a/src/Foo.cs b/src/Foo.cs
            --- a/src/Foo.cs
            +++ b/src/Foo.cs
            @@ -10,3 +10,4 @@
             line
            +added
             line
            """;

        var hunks = DiffHunkParser.Parse(patch);

        Assert.Single(hunks);
        Assert.Equal("src/Foo.cs", hunks[0].File);
        Assert.True(DiffHunkParser.ContainsLine(hunks[0], 11));
    }

    [Fact]
    public void Parse_DeletedFile_UsesOldPathFromMinusHeader()
    {
        const string patch = """
            diff --git a/src/Keep.cs b/src/Keep.cs
            --- a/src/Keep.cs
            +++ b/src/Keep.cs
            @@ -1,1 +1,1 @@
             keep
            diff --git a/src/Gone.cs b/src/Gone.cs
            --- a/src/Gone.cs
            +++ /dev/null
            @@ -1,2 +0,0 @@
            -deleted
            -lines
            """;

        var hunks = DiffHunkParser.Parse(patch);
        Assert.Contains(hunks, hunk => hunk.File == "src/Gone.cs" && hunk.OldLength == 2);
        Assert.Contains(hunks, hunk => hunk.File == "src/Keep.cs");
    }
}

