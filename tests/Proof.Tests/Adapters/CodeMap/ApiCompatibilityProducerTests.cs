using CodeMap.CSharp.Analysis;
using Proof.Adapters.CodeMap;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

public sealed class ApiCompatibilityProducerTests
{
    [Fact]
    public async Task AnalyzeAsync_FingerprintMatch_IsPass()
    {
        var facts = CodeMapPublicSurfaceAnalyzer.Analyze(
            new Dictionary<string, string?> { ["App"] = "abc" },
            new Dictionary<string, string?> { ["App"] = "abc" },
            new Dictionary<string, IReadOnlyList<PublicSurfaceEntry>>(),
            new Dictionary<string, IReadOnlyList<PublicSurfaceEntry>>());
        var producer = new ApiCompatibilityEvidenceProducer(facts);
        var plan = new ProofPlan([
            new ProofObligation("O1", "P001A", ObligationKind.Compatibility, "api", "s1", true, 4, ["r"], new ProofSubject(SubjectKind.ApiSurface, "s1", "App"))
        ]);
        var evidence = await producer.AnalyzeAsync(new ChangeRequest("root", "b", "h", [], SourceDigest: "d"), plan, CancellationToken.None);
        Assert.Equal(EvidenceStatus.Pass, evidence[0].Status);
        Assert.Equal(EvidenceKind.ApiCompatibility, evidence[0].Kind);
    }

    [Fact]
    public async Task AnalyzeAsync_AdditiveSurface_IsPass()
    {
        var baseline = new[] { new PublicSurfaceEntry("Method", "Lib.Foo", "void Foo()", "public") };
        var head = new[]
        {
            new PublicSurfaceEntry("Method", "Lib.Foo", "void Foo()", "public"),
            new PublicSurfaceEntry("Method", "Lib.Bar", "void Bar()", "public")
        };
        var facts = CodeMapPublicSurfaceAnalyzer.Analyze(
            new Dictionary<string, string?> { ["App"] = "old" },
            new Dictionary<string, string?> { ["App"] = "new" },
            new Dictionary<string, IReadOnlyList<PublicSurfaceEntry>> { ["App"] = baseline },
            new Dictionary<string, IReadOnlyList<PublicSurfaceEntry>> { ["App"] = head });
        var producer = new ApiCompatibilityEvidenceProducer(facts);
        var plan = new ProofPlan([
            new ProofObligation("O1", "P001A", ObligationKind.Compatibility, "api", "s1", true, 4, ["r"], new ProofSubject(SubjectKind.ApiSurface, "s1", "App"))
        ]);
        var evidence = await producer.AnalyzeAsync(new ChangeRequest("root", "b", "h", [], SourceDigest: "d"), plan, CancellationToken.None);
        Assert.Equal(EvidenceStatus.Pass, evidence[0].Status);
        var bound = new EvidenceBinder().Bind(plan, evidence, new VerificationPlan(
            [new PlannedVerificationCheck("api-compatibility", "apicompatibility", string.Empty)],
            [],
            "quick"));
        Assert.Equal(ProofVerdict.Proven, new DeterministicProofEvaluator().Evaluate(plan, bound).Verdict);
    }

    [Fact]
    public async Task AnalyzeAsync_Removal_IsFail()
    {
        var baseline = new[] { new PublicSurfaceEntry("Method", "Lib.Foo", "void Foo()", "public") };
        var facts = CodeMapPublicSurfaceAnalyzer.Analyze(
            new Dictionary<string, string?> { ["App"] = "old" },
            new Dictionary<string, string?> { ["App"] = "new" },
            new Dictionary<string, IReadOnlyList<PublicSurfaceEntry>> { ["App"] = baseline },
            new Dictionary<string, IReadOnlyList<PublicSurfaceEntry>> { ["App"] = [] });
        var producer = new ApiCompatibilityEvidenceProducer(facts);
        var plan = new ProofPlan([
            new ProofObligation("O1", "P001A", ObligationKind.Compatibility, "api", "s1", true, 4, ["r"], new ProofSubject(SubjectKind.ApiSurface, "s1", "App"))
        ]);
        var evidence = await producer.AnalyzeAsync(new ChangeRequest("root", "b", "h", [], SourceDigest: "d"), plan, CancellationToken.None);
        Assert.Equal(EvidenceStatus.Fail, evidence[0].Status);
    }

    [Fact]
    public async Task AnalyzeAsync_CallFacts_DoNotReuseConstructorFacts()
    {
        var producer = new ApiCompatibilityEvidenceProducer(
            [new ApiCompatibilityFact("App", ApiCompatibilityState.Unchanged)]);
        var plan = new ProofPlan([
            new ProofObligation("O1", "P001A", ObligationKind.Compatibility, "api", "s1", true, 4, ["r"], new ProofSubject(SubjectKind.ApiSurface, "s1", "App"))
        ]);
        var request = new ChangeRequest("root", "b", "h", [], SourceDigest: "d");
        var breaking = await producer.AnalyzeAsync(
            request,
            plan,
            [new ApiCompatibilityFact("App", ApiCompatibilityState.Breaking)],
            CancellationToken.None);
        var unchanged = await producer.AnalyzeAsync(request, plan, CancellationToken.None);
        Assert.Equal(EvidenceStatus.Fail, breaking[0].Status);
        Assert.Equal(EvidenceStatus.Pass, unchanged[0].Status);
    }

    [Fact]
    public void AnalyzeAsync_FingerprintMismatchWithoutEntries_IsInconclusive()
    {
        Assert.Equal(
            EvidenceStatus.Inconclusive,
            Proof.Adapters.CodeMap.ApiCompatibilityEvidenceProducer.ResolveStatus("a", "b"));
    }
}
