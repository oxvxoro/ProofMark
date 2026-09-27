using CodeMap.CSharp.Analysis;
using CodeMap.Core.Models;

namespace CodeMap.Core.Tests;

public sealed class PublicSurfaceComparerTests
{
    [Fact]
    public void Capture_ThenCompute_MatchesDirectCompute()
    {
        var nodes = new[]
        {
            new CodeNode
            {
                Id = "a",
                Kind = NodeKind.Method,
                Name = "Foo",
                QualifiedName = "Lib.Foo",
                Language = "csharp",
                Signature = "void Foo()",
                Visibility = "public"
            }
        };
        var captured = PublicSurfaceFingerprinter.Capture(nodes);
        Assert.Equal(PublicSurfaceFingerprinter.Compute(nodes), PublicSurfaceFingerprinter.Compute(captured));
    }

    [Fact]
    public void Compare_DetectsBreakingAndAdditive()
    {
        var baseline = new[] { new PublicSurfaceEntry("Method", "Lib.Foo", "void Foo()", "public") };
        var breaking = PublicSurfaceComparer.Compare(baseline, []);
        Assert.Equal(PublicSurfaceChangeKind.Breaking, breaking.Kind);

        var additive = PublicSurfaceComparer.Compare(
            baseline,
            [
                new PublicSurfaceEntry("Method", "Lib.Foo", "void Foo()", "public"),
                new PublicSurfaceEntry("Method", "Lib.Bar", "void Bar()", "public")
            ]);
        Assert.Equal(PublicSurfaceChangeKind.Additive, additive.Kind);
        Assert.True(EvidencePass(additive));
    }

    [Fact]
    public void Compare_DistinguishesOverloadsBySignature()
    {
        var parameterless = new PublicSurfaceEntry(
            "Method",
            "Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder",
            "CreateApplicationBuilder()",
            "public");
        var withArgs = new PublicSurfaceEntry(
            "Method",
            "Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder",
            "CreateApplicationBuilder(System.String[])",
            "public");

        var unchanged = PublicSurfaceComparer.Compare([parameterless, withArgs], [withArgs, parameterless]);
        Assert.Equal(PublicSurfaceChangeKind.Unchanged, unchanged.Kind);

        var added = PublicSurfaceComparer.Compare([parameterless], [parameterless, withArgs]);
        Assert.Equal(PublicSurfaceChangeKind.Additive, added.Kind);
        Assert.Equal([withArgs], added.Added);

        var removed = PublicSurfaceComparer.Compare([parameterless, withArgs], [withArgs]);
        Assert.Equal(PublicSurfaceChangeKind.Breaking, removed.Kind);
        Assert.Equal([parameterless], removed.Removed);
    }

    [Fact]
    public void Compare_SignatureChangeOfSingleMember_IsBreaking()
    {
        var diff = PublicSurfaceComparer.Compare(
            [new PublicSurfaceEntry("Method", "Lib.Foo", "void Foo()", "public")],
            [new PublicSurfaceEntry("Method", "Lib.Foo", "void Foo(int)", "public")]);
        Assert.Equal(PublicSurfaceChangeKind.Breaking, diff.Kind);
        Assert.Equal("void Foo(int)", Assert.Single(diff.Modified).Signature);
    }

    [Fact]
    public void Compare_DuplicateIdenticalEntries_DoNotThrow()
    {
        var entry = new PublicSurfaceEntry("Method", "Lib.Foo", "void Foo()", "public", "id-1");
        var duplicate = entry with { Id = "id-2" };
        var diff = PublicSurfaceComparer.Compare([entry, duplicate], [duplicate]);
        Assert.Equal(PublicSurfaceChangeKind.Unchanged, diff.Kind);
    }

    private static bool EvidencePass(PublicSurfaceDiff diff)
        => diff.Kind is PublicSurfaceChangeKind.Unchanged or PublicSurfaceChangeKind.Additive;
}
