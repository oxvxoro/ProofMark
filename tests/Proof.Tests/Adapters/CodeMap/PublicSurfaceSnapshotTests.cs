using CodeMap.Core.Models;
using CodeMap.CSharp.Analysis;
using Proof.Adapters.CodeMap;
using Proof.Adapters.CodeMap.Sessions;
using Proof.Tests.Fakes;

namespace Proof.Tests;

public sealed class PublicSurfaceSnapshotTests
{
    [Fact]
    public void TryLoad_RoundTrips_AndRejectsCorruptJson()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-surface-" + Guid.NewGuid().ToString("N"));
        try
        {
            var entries = new Dictionary<string, IReadOnlyList<PublicSurfaceEntry>>(StringComparer.Ordinal)
            {
                ["App"] =
                [
                    new PublicSurfaceEntry("Method", "App.Foo", "void Foo()", "public", "id-1")
                ]
            };

            PublicSurfaceSnapshotStore.TrySave(root, "abc123", entries);
            var loaded = PublicSurfaceSnapshotStore.TryLoad(root, "abc123");
            Assert.NotNull(loaded);
            Assert.Single(loaded!["App"]);
            Assert.Equal("App.Foo", loaded["App"][0].QualifiedName);

            var snapshotPath = Path.Combine(root, ".proof", "surface", "abc123.json");
            File.WriteAllText(snapshotPath, "{ not json");
            Assert.Null(PublicSurfaceSnapshotStore.TryLoad(root, "abc123"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void IsResolvableRevision_RejectsWorktree()
    {
        Assert.False(PublicSurfaceSnapshotStore.IsResolvableRevision("WORKTREE"));
        Assert.False(PublicSurfaceSnapshotStore.IsResolvableRevision(null));
        Assert.True(PublicSurfaceSnapshotStore.IsResolvableRevision("deadbeef"));
    }

    [Fact]
    public void TryLoad_DropsExternalAssemblySurface()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-surface-external-" + Guid.NewGuid().ToString("N"));
        try
        {
            const string external = "external:Microsoft.Extensions.Hosting@10.0.0.0";
            var entries = new Dictionary<string, IReadOnlyList<PublicSurfaceEntry>>(StringComparer.Ordinal)
            {
                ["App"] = [new PublicSurfaceEntry("Method", "App.Foo", "void Foo()", "public", "id-1")],
                [external] =
                [
                    new PublicSurfaceEntry(
                        "Method",
                        "Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder",
                        "CreateApplicationBuilder()",
                        "public"),
                    new PublicSurfaceEntry(
                        "Method",
                        "Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder",
                        "CreateApplicationBuilder(System.String[])",
                        "public")
                ]
            };

            PublicSurfaceSnapshotStore.TrySave(root, "abc123", entries);
            var loaded = PublicSurfaceSnapshotStore.TryLoad(root, "abc123");
            Assert.NotNull(loaded);
            Assert.Single(loaded!);
            Assert.True(loaded.ContainsKey("App"));
            Assert.False(loaded.ContainsKey(external));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CaptureEntries_SkipsExternalProjects()
    {
        var app = new IndexedSymbol(
            "app", "App", "file", "App/Foo.cs", NodeKind.Method, "Foo", "App.Foo", "Foo()", 1, 2, "public", "csharp");
        var external = new IndexedSymbol(
            "ext",
            "external:Microsoft.Extensions.Hosting@10.0.0.0",
            "file",
            "external/Microsoft.Extensions.Hosting.dll",
            NodeKind.Method,
            "CreateApplicationBuilder",
            "Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder",
            "CreateApplicationBuilder()",
            null,
            null,
            "public",
            "csharp");
        var captured = ImpactBaselineSession.CaptureEntries(new FakeCodeMapGraphReader([app, external]));
        Assert.Single(captured);
        Assert.Equal("App.Foo", Assert.Single(captured["App"]).QualifiedName);
    }

    [Fact]
    public void TryLoad_RoundTripsEmptySurface()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-surface-empty-" + Guid.NewGuid().ToString("N"));
        try
        {
            PublicSurfaceSnapshotStore.TrySave(
                root,
                "empty-sha",
                new Dictionary<string, IReadOnlyList<PublicSurfaceEntry>>(StringComparer.Ordinal));

            var loaded = PublicSurfaceSnapshotStore.TryLoad(root, "empty-sha");
            Assert.NotNull(loaded);
            Assert.Empty(loaded!);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Producer_FingerprintMatchWithDifferentBaselineEntries_IsNotPass()
    {
        var baseline = new[] { new PublicSurfaceEntry("Method", "App.Foo", "void Foo()", "public") };
        var head = Array.Empty<PublicSurfaceEntry>();
        var facts = CodeMapPublicSurfaceAnalyzer.Analyze(
            new Dictionary<string, string?> { ["App"] = PublicSurfaceFingerprinter.Compute(baseline) },
            new Dictionary<string, string?> { ["App"] = PublicSurfaceFingerprinter.Compute(head) },
            new Dictionary<string, IReadOnlyList<PublicSurfaceEntry>> { ["App"] = baseline },
            new Dictionary<string, IReadOnlyList<PublicSurfaceEntry>> { ["App"] = head });
        var producer = new ApiCompatibilityEvidenceProducer(facts);
        var plan = new Proof.Core.ProofPlan([
            new Proof.Core.ProofObligation(
                "O1",
                "P001A",
                Proof.Core.ObligationKind.Compatibility,
                "api",
                "s1",
                true,
                4,
                ["r"],
                new Proof.Core.ProofSubject(Proof.Core.SubjectKind.ApiSurface, "s1", "App"))
        ]);
        var evidence = await producer.AnalyzeAsync(
            new Proof.Core.ChangeRequest("root", "base", "head", [], SourceDigest: "d"),
            plan,
            CancellationToken.None);
        Assert.Equal(Proof.Core.EvidenceStatus.Fail, evidence[0].Status);
    }
}
