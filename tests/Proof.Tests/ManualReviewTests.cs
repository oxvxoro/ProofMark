using Proof.Cli;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

[Collection("AttestationEnvironment")]
public sealed class ManualReviewTests
{
    private const string Key = "manual-review-key";

    [Fact]
    public void ToPolicy_AcceptsManualReviewEffect()
    {
        var config = new ProofConfig();
        config.Policy.Paths.Add(new PathRuleSection { Match = "assets/**", Effect = "manual-review" });

        var policy = config.ToPolicy();

        Assert.Contains(policy.PathRules!, rule => rule.Effect == "manual-review");
    }

    [Fact]
    public void PathPolicy_ManualReview_MatchesGlob_AndIgnoreStaysUnchanged()
    {
        var rules = new[] { new PathRule("assets/**", "manual-review") };

        Assert.True(PathPolicy.IsManualReview("assets/logo.png", rules));
        Assert.False(PathPolicy.IsManualReview("src/App.cs", rules));
        Assert.False(PathPolicy.IsIgnored("assets/logo.png", rules));
    }

    [Fact]
    public void ApplyPathPolicy_ManualReviewFile_IsKeptInChangeSet()
    {
        var snapshot = new SourceSnapshot(
            "root",
            "base",
            "head",
            false,
            "src",
            [new FileDelta(FileChangeKind.Modified, null, "assets/logo.png", [], [])],
            ChangeSetIsEmpty: false);

        var filtered = ProofOrchestrator.ApplyPathPolicy(
            snapshot,
            [new PathRule("assets/**", "manual-review")]);

        Assert.Single(filtered.Files);
        Assert.False(filtered.ChangeSetIsEmpty);
    }

    [Fact]
    public void Planner_ManualReviewPathRule_AddsP009()
    {
        var impact = new ChangeImpact(
            "base",
            "head",
            [],
            [],
            [],
            [],
            [],
            "complete",
            false,
            FileDeltas: [new FileDelta(FileChangeKind.Modified, null, "assets/logo.png", [], [])]);
        var policy = new ProofPolicy(PathRules: [new PathRule("assets/**", "manual-review")]);

        var plan = new DeterministicProofPlanner().Plan(impact, policy);

        var obligation = Assert.Single(plan.Obligations, item => item.RuleId == "P009");
        Assert.Equal(ObligationKind.ManualReview, obligation.Kind);
        Assert.Equal("assets/logo.png", obligation.SubjectId);
        Assert.Contains(ProofReasonCodes.ManualReviewRequired, obligation.Reasons);
    }

    [Fact]
    public void Planner_BinaryAndSubmoduleChanges_AddP009()
    {
        var impact = new ChangeImpact(
            "base",
            "head",
            [],
            [],
            [],
            [],
            [],
            "complete",
            false,
            FileDeltas:
            [
                new FileDelta(FileChangeKind.BinaryModified, null, "assets/app.png", [], []),
                new FileDelta(FileChangeKind.SubmoduleChanged, null, "vendor/lib", [], [])
            ]);

        var plan = new DeterministicProofPlanner().Plan(impact);

        Assert.Equal(2, plan.Obligations.Count(item => item.RuleId == "P009"));
    }

    [Fact]
    public async Task Producer_SignedReview_EmitsEvidence_AndClosesP009()
    {
        Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, Key);
        var root = Path.Combine(Path.GetTempPath(), "proof-review-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteReview(root, "src", "assets/logo.png", sign: true);
            var plan = PlanWithP009("assets/logo.png");
            var producer = new ManualReviewEvidenceProducer();

            var evidence = await producer.AnalyzeAsync(
                new ChangeRequest(root, "base", "head", [], SourceDigest: "src"), plan, CancellationToken.None);

            var item = Assert.Single(evidence);
            Assert.Equal(EvidenceKind.ManualReview, item.Kind);
            Assert.Equal(EvidenceStatus.Pass, item.Status);
            Assert.Equal("manual-review", item.Provenance.CheckId);

            var bound = new EvidenceBinder().Bind(plan, evidence);
            var evaluation = new DeterministicProofEvaluator().Evaluate(plan, bound);
            Assert.Equal(ObligationStatus.Proven, evaluation.Obligations[0].Status);
            Assert.Equal(ProofVerdict.Proven, evaluation.Verdict);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, null);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Producer_UnsignedReview_EmitsNothing()
    {
        Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, Key);
        var root = Path.Combine(Path.GetTempPath(), "proof-review-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteReview(root, "src", "assets/logo.png", sign: false);
            var evidence = await new ManualReviewEvidenceProducer().AnalyzeAsync(
                new ChangeRequest(root, "base", "head", [], SourceDigest: "src"),
                PlanWithP009("assets/logo.png"),
                CancellationToken.None);

            Assert.Empty(evidence);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, null);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Producer_ForgedSignature_EmitsNothing()
    {
        Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, Key);
        var root = Path.Combine(Path.GetTempPath(), "proof-review-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteReview(root, "src", "assets/logo.png", signature: new string('a', 64));
            var evidence = await new ManualReviewEvidenceProducer().AnalyzeAsync(
                new ChangeRequest(root, "base", "head", [], SourceDigest: "src"),
                PlanWithP009("assets/logo.png"),
                CancellationToken.None);

            Assert.Empty(evidence);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, null);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Producer_StaleDigestReview_EmitsNothing()
    {
        Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, Key);
        var root = Path.Combine(Path.GetTempPath(), "proof-review-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteReview(root, "old-digest", "assets/logo.png", sign: true);
            var evidence = await new ManualReviewEvidenceProducer().AnalyzeAsync(
                new ChangeRequest(root, "base", "head", [], SourceDigest: "src"),
                PlanWithP009("assets/logo.png"),
                CancellationToken.None);

            Assert.Empty(evidence);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, null);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Producer_V2Review_BindsReviewer_AndClosesP009()
    {
        Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, Key);
        var root = Path.Combine(Path.GetTempPath(), "proof-review-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteReviewV2(root, "src", "assets/logo.png", "alice@example.com");
            var evidence = await new ManualReviewEvidenceProducer().AnalyzeAsync(
                new ChangeRequest(root, "base", "head", [], SourceDigest: "src"),
                PlanWithP009("assets/logo.png"),
                CancellationToken.None);

            Assert.Single(evidence);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, null);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Producer_V2Review_ChangedReviewer_EmitsNothing()
    {
        Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, Key);
        var root = Path.Combine(Path.GetTempPath(), "proof-review-" + Guid.NewGuid().ToString("N"));
        try
        {
            // 서명은 alice에 대해 계산되지만 파일에는 mallory가 기록된다.
            // reviewer는 서명된 페이로드의 일부이므로 이를 변조하면 실패한다.
            WriteReviewV2(root, "src", "assets/logo.png", "alice@example.com", storedReviewer: "mallory@example.com");
            var evidence = await new ManualReviewEvidenceProducer().AnalyzeAsync(
                new ChangeRequest(root, "base", "head", [], SourceDigest: "src"),
                PlanWithP009("assets/logo.png"),
                CancellationToken.None);

            Assert.Empty(evidence);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, null);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Producer_V1Review_Rejected_WhenOnlySchema2Accepted()
    {
        Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, Key);
        var root = Path.Combine(Path.GetTempPath(), "proof-review-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteReview(root, "src", "assets/logo.png", sign: true);
            var evidence = await new ManualReviewEvidenceProducer([ReviewSignature.ReviewerBoundSchemaVersion]).AnalyzeAsync(
                new ChangeRequest(root, "base", "head", [], SourceDigest: "src"),
                PlanWithP009("assets/logo.png"),
                CancellationToken.None);

            Assert.Empty(evidence);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, null);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Producer_V2Review_Rejected_WhenOnlySchema1Accepted()
    {
        Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, Key);
        var root = Path.Combine(Path.GetTempPath(), "proof-review-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteReviewV2(root, "src", "assets/logo.png", "alice@example.com");
            var evidence = await new ManualReviewEvidenceProducer([ReviewSignature.LegacySchemaVersion]).AnalyzeAsync(
                new ChangeRequest(root, "base", "head", [], SourceDigest: "src"),
                PlanWithP009("assets/logo.png"),
                CancellationToken.None);

            Assert.Empty(evidence);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, null);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReviewSignature_V2_BindsReviewer()
    {
        Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, Key);
        try
        {
            var signature = AttestationHmac.ComputeHex(
                Key, AttestationHmac.ReviewPayloadV2("src", "assets/logo.png", "alice@example.com"));

            Assert.True(ReviewSignature.IsValidV2("src", "assets/logo.png", "alice@example.com", signature));
            Assert.False(ReviewSignature.IsValidV2("src", "assets/logo.png", "mallory@example.com", signature));
        }
        finally
        {
            Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, null);
        }
    }

    [Fact]
    public void Binder_FreeTextManualReviewEvidence_IsRejected()
    {
        var plan = PlanWithP009("assets/logo.png");
        var freeText = new ProofEvidence(
            "MR1",
            EvidenceKind.ManualReview,
            "assets/logo.png",
            EvidenceStatus.Pass,
            new EvidenceProvenance("manual-review", CheckId: "manual-review", SourceDigest: "src"),
            new EvidenceScope(ScopeMode.Contains, Subjects: ["assets/logo.png"]));

        var bound = new EvidenceBinder().Bind(plan, [freeText]);

        Assert.Empty(bound.Links);
        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, bound);
        Assert.Equal(ObligationStatus.Unresolved, evaluation.Obligations[0].Status);
    }

    private static ProofPlan PlanWithP009(string subjectId)
        => new(
            [new ProofObligation("O1", "P009", ObligationKind.ManualReview, "review", subjectId, true, 3, ["r"],
                new ProofSubject(SubjectKind.File, subjectId, File: subjectId, DisplayName: subjectId))],
            SourceDigest: "src");

    private static void WriteReview(
        string root,
        string statementDigest,
        string subjectId,
        bool sign = false,
        string? signature = null)
    {
        var directory = Path.Combine(root, ".proof", "reviews");
        Directory.CreateDirectory(directory);
        var payload = AttestationHmac.ReviewPayload(statementDigest, subjectId);
        var resolvedSignature = signature
            ?? (sign ? AttestationHmac.ComputeHex(Key, payload) : null);
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            subjectId,
            statementDigest,
            signature = resolvedSignature,
            reviewer = "reviewer@example.com"
        });
        File.WriteAllText(Path.Combine(directory, "review.json"), json);
    }

    private static void WriteReviewV2(
        string root,
        string statementDigest,
        string subjectId,
        string reviewer,
        string? storedReviewer = null)
    {
        var directory = Path.Combine(root, ".proof", "reviews");
        Directory.CreateDirectory(directory);
        var signature = AttestationHmac.ComputeHex(
            Key, AttestationHmac.ReviewPayloadV2(statementDigest, subjectId, reviewer));
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            schemaVersion = 2,
            subjectId,
            statementDigest,
            signature,
            reviewer = storedReviewer ?? reviewer
        });
        File.WriteAllText(Path.Combine(directory, "review.json"), json);
    }
}