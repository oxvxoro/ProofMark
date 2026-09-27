using Proof.Cli;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

[Collection("WorkingDirectory")]
public sealed class ReviewCommandTests : IDisposable
{
    private const string Key = "review-command-key";
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "proof-review-cmd-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workspace, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private static ProofPlan PlanWithP009(string subjectId)
        => new(
            [new ProofObligation("O1", "P009", ObligationKind.ManualReview, "review", subjectId, true, 3, ["r"],
                new ProofSubject(SubjectKind.File, subjectId, File: subjectId, DisplayName: subjectId))],
            SourceDigest: "src");

    [Fact]
    public void Sign_WithoutKey_Returns2()
    {
        Directory.CreateDirectory(_workspace);
        Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, null);
        Directory.SetCurrentDirectory(_workspace);
        try
        {
            var exit = ReviewCommand.ExecuteSignAsync("assets/logo.png", "src", null, null, CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert.Equal(2, exit);
            Assert.Empty(Directory.EnumerateFiles(_workspace, "*.json", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
        }
    }

    [Fact]
    public void Sign_WritesSignedReview_ProducerClosesP009()
    {
        Directory.CreateDirectory(_workspace);
        Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, Key);
        Directory.SetCurrentDirectory(_workspace);
        try
        {
            var exit = ReviewCommand.ExecuteSignAsync("assets/logo.png", "src", "reviewer@example.com", null, CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert.Equal(0, exit);

            var reviewPath = Path.Combine(_workspace, ".proof", "reviews");
            var file = Assert.Single(Directory.EnumerateFiles(reviewPath, "*.json"));
            Assert.EndsWith("-" + "src" + ".json", file, StringComparison.Ordinal);

            var producer = new ManualReviewEvidenceProducer();
            var request = new ChangeRequest(_workspace, "base", "head", [], SourceDigest: "src");
            var evidence = producer.AnalyzeAsync(request, PlanWithP009("assets/logo.png"), CancellationToken.None)
                .GetAwaiter().GetResult();
            var item = Assert.Single(evidence);
            Assert.Equal(EvidenceKind.ManualReview, item.Kind);

            var bound = new EvidenceBinder().Bind(PlanWithP009("assets/logo.png"), evidence);
            var evaluation = new DeterministicProofEvaluator().Evaluate(PlanWithP009("assets/logo.png"), bound);
            Assert.Equal(ObligationStatus.Proven, evaluation.Obligations[0].Status);

            // stdout에 키 자체가 절대 들어가면 안 된다
            Assert.DoesNotContain(Key, File.ReadAllText(file), StringComparison.Ordinal);
        }
        finally
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
            Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, null);
        }
    }

    [Fact]
    public void Sign_WithUnknownSubject_WarnsAndStillExits0()
    {
        Directory.CreateDirectory(_workspace);
        Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, Key);
        Directory.SetCurrentDirectory(_workspace);
        try
        {
            // 이 워크스페이스의 어떤 계획도 이 주체에 대한 P009를 갖지 않는다.
            // review 파일은 그래도 쓰인다(저작 도구이며 증거가 아니다).
            var exit = ReviewCommand.ExecuteSignAsync("docs/notes.md", "src", null, null, CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert.Equal(0, exit);
            var file = Assert.Single(Directory.EnumerateFiles(Path.Combine(_workspace, ".proof", "reviews"), "*.json"));
            Assert.Contains("docs/notes.md", File.ReadAllText(file), StringComparison.Ordinal);
        }
        finally
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
            Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, null);
        }
    }

    [Fact]
    public void Sign_IsDeterministic_ForSameSubjectAndDigest()
    {
        Directory.CreateDirectory(_workspace);
        Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, Key);
        Directory.SetCurrentDirectory(_workspace);
        try
        {
            var exit1 = ReviewCommand.ExecuteSignAsync("assets/logo.png", "src", null, null, CancellationToken.None)
                .GetAwaiter().GetResult();
            var exit2 = ReviewCommand.ExecuteSignAsync("assets/logo.png", "src", null, null, CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert.Equal(0, exit1);
            Assert.Equal(0, exit2);

            // 결정적 파일 이름: 같은 주체와 다이제스트를 두 번 서명하면
            // 같은 경로에 쓴다(두 번째 실행이 첫 번째를 덮어쓴다).
            // 서명 바이트도 같다.
            var file = Assert.Single(Directory.EnumerateFiles(Path.Combine(_workspace, ".proof", "reviews"), "*.json"));
            Assert.EndsWith("-" + "src" + ".json", Path.GetFileName(file), StringComparison.Ordinal);
            var first = System.Text.Json.JsonSerializer.Deserialize<ReviewCommand.ReviewFile>(File.ReadAllText(file), ProofJson.WireOptions);
            Assert.Equal("assets/logo.png", first!.SubjectId);
            Assert.Equal(ReviewSignature.ReviewerBoundSchemaVersion, first.SchemaVersion);
            Assert.Equal(
                AttestationHmac.ComputeHex(Key, AttestationHmac.ReviewPayloadV2("src", "assets/logo.png", string.Empty)),
                first.Signature);
        }
        finally
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
            Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, null);
        }
    }

    [Fact]
    public void List_ShowsValidAndInvalid()
    {
        Directory.CreateDirectory(_workspace);
        Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, Key);
        Directory.SetCurrentDirectory(_workspace);
        try
        {
            ReviewCommand.ExecuteSignAsync("assets/logo.png", "src", null, null, CancellationToken.None)
                .GetAwaiter().GetResult();
            var reviews = Path.Combine(_workspace, ".proof", "reviews");
            File.WriteAllText(
                Path.Combine(reviews, "forged.json"),
                """{"subjectId":"assets/other.png","statementDigest":"src","signature":"0000000000000000000000000000000000000000000000000000000000000000"}""");

            var exit = ReviewCommand.ExecuteList(null);
            Assert.Equal(0, exit);
            var valid = ManualReviewVerifier.IsSignatureValid(
                "src", "assets/logo.png",
                AttestationHmac.ComputeHex(Key, AttestationHmac.ReviewPayload("src", "assets/logo.png")));
            Assert.True(valid);
            Assert.False(ManualReviewVerifier.IsSignatureValid("src", "assets/other.png", "0000000000000000000000000000000000000000000000000000000000000000"));
        }
        finally
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
            Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, null);
        }
    }

    [Fact]
    public void List_Certificate_ComparesSourceDigest_NotStatementDigest()
    {
        Directory.CreateDirectory(_workspace);
        Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, Key);
        Directory.SetCurrentDirectory(_workspace);
        try
        {
            Assert.Equal(0, ReviewCommand.ExecuteSignAsync("assets/logo.png", "src", null, null, CancellationToken.None)
                .GetAwaiter().GetResult());

            var obligation = new ProofObligation(
                "O1", "P009", ObligationKind.ManualReview, "review", "assets/logo.png", true, 3, ["r"],
                new ProofSubject(SubjectKind.File, "assets/logo.png", File: "assets/logo.png", DisplayName: "assets/logo.png"));
            var plan = new ProofPlan([obligation], SourceDigest: "src");
            var certificate = new ChangeCertificateBuilder().Build(
                new ChangeImpact("base", "head", [], [], [], [], [], "complete", false, SourceDigest: "src"),
                plan,
                new VerificationEvidenceSet([], []),
                new ProofEvaluation(ProofVerdict.Uncertain, [new EvaluatedObligation(obligation, ObligationStatus.Unresolved, [])]));
            Assert.False(string.Equals(certificate.SourceDigest, certificate.StatementDigest, StringComparison.Ordinal));

            var certPath = Path.Combine(_workspace, "certificate.json");
            File.WriteAllText(certPath, System.Text.Json.JsonSerializer.Serialize(certificate, ProofJson.WireOptions));

            var original = Console.Out;
            using var writer = new StringWriter();
            Console.SetOut(writer);
            try
            {
                Assert.Equal(0, ReviewCommand.ExecuteList(certPath));
            }
            finally
            {
                Console.SetOut(original);
            }

            var output = writer.ToString();
            Assert.Contains("valid", output, StringComparison.Ordinal);
            Assert.DoesNotContain("stale", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
            Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, null);
        }
    }
}
