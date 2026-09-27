using System.Text.Json;
using Proof.Cli;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

[Collection("AttestationEnvironment")]
public sealed class PolicyExceptionTests
{
    private const string Key = "exception-test-key";

    [Fact]
    public void Signature_is_valid_and_detects_tampering()
    {
        WithKey(() =>
        {
            var signed = Sign(Exception("P005", "src/Foo.cs", "owner", "reason"));
            Assert.True(PolicyExceptionSignature.IsValid(signed));
            Assert.False(PolicyExceptionSignature.IsValid(signed with { Reason = "changed" }));
        });

        // 키가 없으면 서명을 검증할 수 없다.
        var orphan = Exception("P005", "src/Foo.cs", "owner", "reason") with { Signature = "deadbeef" };
        Assert.False(PolicyExceptionSignature.IsValid(orphan));
    }

    [Fact]
    public void Governance_state_classifies_unsigned_invalid_expired_and_valid()
    {
        var now = new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero);
        var unsigned = Exception("P005", "subject", "owner", "reason");
        Assert.Equal(PolicyExceptionState.Unsigned, PolicyExceptionGovernance.EvaluateState(unsigned, now));

        WithKey(() =>
        {
            var valid = Sign(Exception("P005", "subject", "owner", "reason", expiresAt: now.AddDays(30)));
            Assert.Equal(PolicyExceptionState.Valid, PolicyExceptionGovernance.EvaluateState(valid, now));

            var expired = Sign(Exception("P005", "subject", "owner", "reason", expiresAt: now.AddDays(-1)));
            Assert.Equal(PolicyExceptionState.Expired, PolicyExceptionGovernance.EvaluateState(expired, now));

            var tampered = valid with { Owner = "someone-else" };
            Assert.Equal(PolicyExceptionState.InvalidSignature, PolicyExceptionGovernance.EvaluateState(tampered, now));
        });
    }

    [Fact]
    public void Governance_state_is_unverifiable_without_key()
    {
        PolicyException? signed = null;
        WithKey(() => signed = Sign(Exception("P005", "subject", "owner", "reason")));

        // 키는 지워졌다. 서명된 예외가 무효로 보고되면 안 된다.
        Assert.Equal(PolicyExceptionState.Unverifiable, PolicyExceptionGovernance.EvaluateState(signed!, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Covers_matches_rule_subject_and_source()
    {
        var ruleWide = Exception("P005", PolicyExceptionSignature.AllSubjects, "owner", "reason");
        Assert.True(PolicyExceptionGovernance.Covers(ruleWide, "P005", "src/Any.cs", "digest"));
        Assert.False(PolicyExceptionGovernance.Covers(ruleWide, "P009", "src/Any.cs", "digest"));

        var subjectBound = Exception("P005", "src/Foo.cs", "owner", "reason", sourceDigest: "digest");
        Assert.True(PolicyExceptionGovernance.Covers(subjectBound, "P005", "src/Foo.cs", "digest"));
        Assert.False(PolicyExceptionGovernance.Covers(subjectBound, "P005", "src/Bar.cs", "digest"));
        Assert.False(PolicyExceptionGovernance.Covers(subjectBound, "P005", "src/Foo.cs", "other-digest"));
        Assert.False(PolicyExceptionGovernance.Covers(subjectBound, "P005", "src/Foo.cs", null));

        var digestFree = Exception("P005", "src/Foo.cs", "owner", "reason");
        Assert.True(PolicyExceptionGovernance.Covers(digestFree, "P005", "src/Foo.cs", "any-digest"));
    }

    [Fact]
    public void Evaluator_reports_only_required_open_obligations()
    {
        var certificate = Certificate(
            "digest",
            ("P005", "src/Foo.cs", true, ObligationStatus.Unresolved),
            ("P009", "src/Bar.cs", true, ObligationStatus.Unresolved),
            ("P003", "src/Baz.cs", true, ObligationStatus.Proven));

        ExceptionEvaluationReport? report = null;
        WithKey(() => report = PolicyExceptionEvaluator.Evaluate(
            certificate,
            "cert.json",
            [new LoadedPolicyException("p005.json", Sign(Exception("P005", "src/Foo.cs", "owner", "reason", sourceDigest: "digest")))]));

        Assert.NotNull(report);
        Assert.Equal(2, report.RequiredOpenObligations);
        Assert.Equal(1, report.Covered);
        Assert.Equal(1, report.Uncovered);
        Assert.Equal("NOT_READY", report.Verdict);
    }

    [Fact]
    public void Gate_clears_only_obligation_driven_blocks()
    {
        Assert.True(PolicyExceptionEvaluator.CanClearMergeBlock(Report(open: 1, uncovered: 0, constraints: 0)));
        // 열린 의무가 없다는 것은 차단이 의무 때문에 생긴 것이 아니라는 뜻이다.
        Assert.False(PolicyExceptionEvaluator.CanClearMergeBlock(Report(open: 0, uncovered: 0, constraints: 0)));
        Assert.False(PolicyExceptionEvaluator.CanClearMergeBlock(Report(open: 1, uncovered: 1, constraints: 0)));
        // 차단 제약(영향 절단 / 무결성 코드)은 절대 예외로 둘 수 없다.
        Assert.False(PolicyExceptionEvaluator.CanClearMergeBlock(Report(open: 1, uncovered: 0, constraints: 1)));
    }

    [Fact]
    public void Store_loads_valid_and_records_invalid_json()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-exceptions-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, ".proof", "exceptions");
        Directory.CreateDirectory(directory);
        try
        {
            WithKey(() =>
            {
                var signed = Sign(Exception("P005", "src/Foo.cs", "owner", "reason"));
                File.WriteAllText(Path.Combine(directory, "p005.json"), JsonSerializer.Serialize(signed, ProofJson.WireOptions));
            });
            File.WriteAllText(Path.Combine(directory, "bad.json"), "{ not json");

            var loaded = PolicyExceptionStore.Load(root);

            Assert.Equal(2, loaded.Count);
            Assert.Contains(loaded, item => item.Value?.RuleId == "P005");
            Assert.Contains(loaded, item => item.Value is null);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WithKey(Action action)
    {
        Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, Key);
        try
        {
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable, null);
        }
    }

    private static PolicyException Sign(PolicyException exception)
        => exception with { Signature = PolicyExceptionSignature.Sign(exception) };

    private static ExceptionEvaluationReport Report(int open, int uncovered, int constraints)
        => new("cert.json", "NOT_READY", "digest", open, open - uncovered, uncovered, constraints, []);

    private static PolicyException Exception(
        string ruleId,
        string subjectId,
        string owner,
        string reason,
        string? sourceDigest = null,
        DateTimeOffset? expiresAt = null)
        => new(ruleId, subjectId, owner, reason, Ticket: "SEC-1", SourceDigest: sourceDigest, ExpiresAt: expiresAt, Signer: "tester");

    private static ChangeCertificate Certificate(
        string sourceDigest,
        params (string RuleId, string SubjectId, bool Required, ObligationStatus Status)[] obligations)
    {
        var plan = new ProofPlan(
            [.. obligations.Select((item, index) => new ProofObligation(
                $"OBL-{index}",
                item.RuleId,
                ObligationKind.ManualReview,
                "claim",
                item.SubjectId,
                item.Required,
                1,
                []))]);
        var evaluation = new ProofEvaluation(
            ProofVerdict.NotReady,
            [.. obligations.Select((item, index) => new EvaluatedObligation(plan.Obligations[index], item.Status, []))]);
        return new ChangeCertificate(
            3,
            "base",
            "head",
            ProofVerdict.NotReady,
            new ChangeImpact("base", "head", [], [], [], [], [], "complete", false),
            plan,
            new VerificationEvidenceSet([], []),
            evaluation,
            SourceDigest: sourceDigest);
    }
}
