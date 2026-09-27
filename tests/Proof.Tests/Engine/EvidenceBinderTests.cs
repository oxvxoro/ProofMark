using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

public sealed class EvidenceBinderTests
{
    [Fact]
    public void Bind_UnrelatedTestPass_DoesNotLinkImpactedTest()
    {
        var plan = new ProofPlan([
            Obligation("O1", ObligationKind.Test, "TestA", displayName: "Tests.OrderServiceTests.Cancel_keeps_order")
        ]);
        var evidence = new[]
        {
            Evidence(
                "E1",
                EvidenceKind.TestRun,
                EvidenceStatus.Pass,
                "unit",
                commandTarget: "Other.Tests.csproj",
                scopeSubjects: ["Other.Tests.Unrelated.Fact"],
                checkId: "unit")
        };

        var bound = new EvidenceBinder().Bind(plan, evidence);

        Assert.Empty(bound.Links);
        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, bound);
        Assert.Equal(ProofVerdict.Uncertain, evaluation.Verdict);
        Assert.Equal(ObligationStatus.Unresolved, evaluation.Obligations[0].Status);
    }

    [Fact]
    public void Bind_MatchingFailedTestCase_ReturnsNotReady()
    {
        var plan = new ProofPlan([
            Obligation("O1", ObligationKind.Test, "sym-test-a", displayName: "Tests.OrderServiceTests.Cancel_keeps_order")
        ]);
        var evidence = new[]
        {
            Evidence(
                "E1",
                EvidenceKind.TestCase,
                EvidenceStatus.Fail,
                "Tests.OrderServiceTests.Cancel_keeps_order",
                scopeSubjects: ["Tests.OrderServiceTests.Cancel_keeps_order"],
                checkId: "unit",
                subjectRefs:
                [
                    new EvidenceSubjectRef(
                        SubjectKind.Test,
                        "sym-test-a",
                        FullyQualifiedName: "Tests.OrderServiceTests.Cancel_keeps_order")
                ])
        };

        var bound = new EvidenceBinder().Bind(plan, evidence);
        Assert.Contains(bound.Links, link => link.ObligationId == "O1" && link.Relation == "direct");

        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, bound);
        Assert.Equal(ProofVerdict.NotReady, evaluation.Verdict);
        Assert.Equal(ObligationStatus.Failed, evaluation.Obligations[0].Status);
    }

    [Fact]
    public void Bind_TestRunPassWithSkippedCase_CannotDirectlyProve()
    {
        var plan = new ProofPlan([
            Obligation("O1", ObligationKind.Test, "Tests.OrderServiceTests.Cancel_keeps_order", displayName: "Tests.OrderServiceTests.Cancel_keeps_order")
        ]);
        var evidence = new[]
        {
            Evidence(
                "E1",
                EvidenceKind.TestRun,
                EvidenceStatus.Pass,
                "unit",
                scopeSubjects: ["Tests.OrderServiceTests.Cancel_keeps_order"],
                checkId: "unit",
                subjectRefs:
                [
                    new EvidenceSubjectRef(
                        SubjectKind.Test,
                        "Tests.OrderServiceTests.Cancel_keeps_order",
                        FullyQualifiedName: "Tests.OrderServiceTests.Cancel_keeps_order")
                ]),
            Evidence(
                "E2",
                EvidenceKind.TestCase,
                EvidenceStatus.Skipped,
                "Tests.OrderServiceTests.Cancel_keeps_order",
                scopeSubjects: ["Tests.OrderServiceTests.Cancel_keeps_order"],
                checkId: "unit",
                subjectRefs:
                [
                    new EvidenceSubjectRef(
                        SubjectKind.Test,
                        "Tests.OrderServiceTests.Cancel_keeps_order",
                        FullyQualifiedName: "Tests.OrderServiceTests.Cancel_keeps_order")
                ])
        };

        var bound = new EvidenceBinder().Bind(plan, evidence);
        Assert.DoesNotContain(bound.Links, link => link.EvidenceId == "E1" && link.Relation == "direct" && link.Strength >= 3);
        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, bound);
        Assert.NotEqual(ProofVerdict.Proven, evaluation.Verdict);
    }

    [Fact]
    public void Bind_PlanCheckWhitelist_IgnoresUnplannedPass()
    {
        var plan = new ProofPlan([
            Obligation("O1", ObligationKind.Build, "App")
        ]);
        var verificationPlan = new VerificationPlan(
            [new PlannedVerificationCheck("build", "build", "App.csproj")],
            [],
            "quick");
        var evidence = new[]
        {
            Evidence(
                "E1",
                EvidenceKind.Build,
                EvidenceStatus.Pass,
                "extra",
                commandTarget: "src/App/App.csproj",
                checkId: "accidental")
        };

        var bound = new EvidenceBinder().Bind(plan, evidence, verificationPlan);
        Assert.Empty(bound.Links);
        Assert.Equal(ProofVerdict.Uncertain, new DeterministicProofEvaluator().Evaluate(plan, bound).Verdict);
    }

    [Fact]
    public void Bind_TailCollision_IsSupportingOnly()
    {
        var plan = new ProofPlan([
            Obligation("O1", ObligationKind.Test, "Lib.Tests.Cancel_keeps_order", displayName: "Lib.Tests.Cancel_keeps_order")
        ]);
        var evidence = new[]
        {
            Evidence(
                "E1",
                EvidenceKind.TestCase,
                EvidenceStatus.Pass,
                "Other.Tests.Cancel_keeps_order",
                scopeSubjects: ["Other.Tests.Cancel_keeps_order"],
                checkId: "unit")
        };

        var bound = new EvidenceBinder().Bind(plan, evidence);
        Assert.DoesNotContain(bound.Links, link => link.Relation == "direct" && link.Strength >= 3);
        Assert.All(bound.Links, link => Assert.True(link.Strength <= 2));
    }

    [Fact]
    public void Bind_ProjectAndTfmExactness_RequiresMatchingRef()
    {
        var plan = new ProofPlan([
            new ProofObligation(
                "O1",
                "P004",
                ObligationKind.Test,
                "claim",
                "Tests.Foo.Bar",
                true,
                4,
                ["r"],
                new ProofSubject(SubjectKind.Test, "Tests.Foo.Bar", "App.Tests", DisplayName: "Tests.Foo.Bar", TargetFramework: "net10.0"))
        ]);
        var colliding = Evidence(
            "E1",
            EvidenceKind.TestCase,
            EvidenceStatus.Pass,
            "Tests.Foo.Bar",
            checkId: "unit",
            subjectRefs:
            [
                new EvidenceSubjectRef(SubjectKind.Test, "Tests.Foo.Bar", "App.Tests", FullyQualifiedName: "Tests.Foo.Bar", TargetFramework: "net8.0")
            ]);
        var matching = Evidence(
            "E2",
            EvidenceKind.TestCase,
            EvidenceStatus.Pass,
            "Tests.Foo.Bar",
            checkId: "unit",
            subjectRefs:
            [
                new EvidenceSubjectRef(SubjectKind.Test, "Tests.Foo.Bar", "App.Tests", FullyQualifiedName: "Tests.Foo.Bar", TargetFramework: "net10.0")
            ]);

        var boundCollision = new EvidenceBinder().Bind(plan, [colliding]);
        Assert.DoesNotContain(boundCollision.Links, link => link.Relation == "direct" && link.Strength >= 3);
        var boundWrongProject = new EvidenceBinder().Bind(plan, [
            Evidence(
                "E3",
                EvidenceKind.TestCase,
                EvidenceStatus.Pass,
                "Tests.Foo.Bar",
                checkId: "unit",
                subjectRefs:
                [
                    new EvidenceSubjectRef(SubjectKind.Test, "Tests.Foo.Bar", "Other.Tests", FullyQualifiedName: "Tests.Foo.Bar", TargetFramework: "net10.0")
                ])
        ]);
        Assert.DoesNotContain(boundWrongProject.Links, link => link.Relation == "direct" && link.Strength >= 3);
        var boundMatch = new EvidenceBinder().Bind(plan, [matching]);
        Assert.Contains(boundMatch.Links, link => link.Relation == "direct" && link.Strength >= 3);
    }

    [Fact]
    public void Bind_GenericBuild_IsSupportingOnlyForCompatibility()
    {
        var plan = new ProofPlan([
            Obligation("O1", ObligationKind.Compatibility, "public-api")
        ]);
        var evidence = new[]
        {
            Evidence(
                "E1",
                EvidenceKind.Build,
                EvidenceStatus.Pass,
                "build",
                commandTarget: "Proof.slnx",
                checkId: "build")
        };

        var bound = new EvidenceBinder().Bind(plan, evidence);
        Assert.Contains(bound.Links, link => link.Relation == "supporting" && link.Strength == 2);

        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, bound);
        Assert.Equal(ProofVerdict.Uncertain, evaluation.Verdict);
        Assert.Equal(ObligationStatus.Unresolved, evaluation.Obligations[0].Status);
    }

    [Fact]
    public void Bind_StaleSourceDigest_IsRejected()
    {
        var plan = new ProofPlan([
            Obligation("O1", ObligationKind.Build, "App")
        ], SourceDigest: "digest-a");
        var evidence = new[]
        {
            Evidence(
                "E1",
                EvidenceKind.Build,
                EvidenceStatus.Pass,
                "build",
                sourceDigest: "digest-b",
                commandTarget: "App/App.csproj",
                checkId: "build")
        };

        var bound = new EvidenceBinder().Bind(plan, evidence);
        Assert.Contains(bound.Links, link =>
            link.Relation == "rejected"
            && link.Strength == 0
            && link.ReasonCode == ProofReasonCodes.EvidenceStaleSource);
        Assert.DoesNotContain(bound.Links, link => link.Relation == "direct");
    }

    [Fact]
    public void Bind_MissingSourceDigest_IsRejectedWhenPlanHasDigest()
    {
        var plan = new ProofPlan([
            Obligation("O1", ObligationKind.Build, "App")
        ], SourceDigest: "digest-a");
        var evidence = new[]
        {
            Evidence(
                "E1",
                EvidenceKind.Build,
                EvidenceStatus.Pass,
                "build",
                commandTarget: "src/App/App.csproj",
                checkId: "build")
        };

        var bound = new EvidenceBinder().Bind(plan, evidence);
        Assert.Contains(bound.Links, link =>
            link.Relation == "rejected"
            && link.Strength == 0
            && link.ReasonCode == ProofReasonCodes.EvidenceSourceIdentityMissing);
        Assert.DoesNotContain(bound.Links, link => link.Relation == "direct");
    }

    [Fact]
    public void Bind_ExactProjectBuild_IsDirect()
    {
        var plan = new ProofPlan([
            Obligation("O1", ObligationKind.CrossProject, "App")
        ]);
        var evidence = new[]
        {
            Evidence(
                "E1",
                EvidenceKind.Build,
                EvidenceStatus.Pass,
                "build",
                commandTarget: "src/App/App.csproj",
                checkId: "build")
        };

        var bound = new EvidenceBinder().Bind(plan, evidence);
        Assert.Contains(bound.Links, link => link.Relation == "direct" && link.Strength == 3);
        Assert.Equal(ProofVerdict.Proven, new DeterministicProofEvaluator().Evaluate(plan, bound).Verdict);
    }

    [Fact]
    public void Bind_TestCaseFqnSuffixMatch_DisplayNameWithSignature_IsDirect()
    {
        var plan = new ProofPlan([
            Obligation("O1", ObligationKind.Test, "sym-1", displayName: "App.Tests.OrderServiceTests.Cancel_keeps_order")
        ]);
        var evidence = new[]
        {
            Evidence(
                "E1",
                EvidenceKind.TestCase,
                EvidenceStatus.Pass,
                "unit",
                checkId: "unit",
                subjectRefs:
                [
                    new EvidenceSubjectRef(
                        SubjectKind.Test,
                        "App.Tests.OrderServiceTests.Cancel_keeps_order",
                        FullyQualifiedName: "App.Tests.OrderServiceTests.Cancel_keeps_order",
                        Project: "App.Tests")
                ])
        };

        var bound = new EvidenceBinder().Bind(plan, evidence);
        Assert.Contains(bound.Links, link => link.ObligationId == "O1" && link.Relation == "direct");
    }

    [Fact]
    public void Bind_TestCaseFqnSuffixMatch_NamespaceLessDisplayName_IsDirect()
    {
        var plan = new ProofPlan([
            Obligation("O1", ObligationKind.Test, "sym-1", displayName: "OrderServiceTests.Cancel_keeps_order")
        ]);
        var evidence = new[]
        {
            Evidence(
                "E1",
                EvidenceKind.TestCase,
                EvidenceStatus.Pass,
                "unit",
                checkId: "unit",
                subjectRefs:
                [
                    new EvidenceSubjectRef(
                        SubjectKind.Test,
                        "App.Tests.OrderServiceTests.Cancel_keeps_order",
                        FullyQualifiedName: "App.Tests.OrderServiceTests.Cancel_keeps_order")
                ])
        };

        var bound = new EvidenceBinder().Bind(plan, evidence);
        Assert.Contains(bound.Links, link => link.ObligationId == "O1" && link.Relation == "direct");
    }

    [Fact]
    public void Bind_TestCaseUnrelatedFqnSharingShortMethodName_IsNotDirect()
    {
        var plan = new ProofPlan([
            Obligation("O1", ObligationKind.Test, "sym-1", displayName: "OrderServiceTests.Run")
        ]);
        var evidence = new[]
        {
            Evidence(
                "E1",
                EvidenceKind.TestCase,
                EvidenceStatus.Pass,
                "unit",
                checkId: "unit",
                subjectRefs:
                [
                    new EvidenceSubjectRef(
                        SubjectKind.Test,
                        "Other.Tests.UnrelatedTests.Run",
                        FullyQualifiedName: "Other.Tests.UnrelatedTests.Run")
                ])
        };

        var bound = new EvidenceBinder().Bind(plan, evidence);
        Assert.DoesNotContain(bound.Links, link => link.ObligationId == "O1" && link.Relation == "direct");
    }

    [Fact]
    public void Bind_TestCaseNameOnlyWithoutFqn_IsSupportingOnly()
    {
        // FQN 없이 메서드 Name만 보고하는 잘못된 실행은 뒷받침할
        // 수는 있지만, 테스트 의무를 절대 직접 증명하면 안 된다.
        var plan = new ProofPlan([
            Obligation("O1", ObligationKind.Test, "sym-1", displayName: "App.Tests.OrderServiceTests.Cancel_keeps_order")
        ]);
        var evidence = new[]
        {
            Evidence(
                "E1",
                EvidenceKind.TestCase,
                EvidenceStatus.Pass,
                "Cancel_keeps_order",
                scopeSubjects: ["Cancel_keeps_order"],
                checkId: "unit")
        };

        var bound = new EvidenceBinder().Bind(plan, evidence);

        Assert.DoesNotContain(bound.Links, link => link.Relation == "direct" && link.Strength >= 3);
        Assert.All(bound.Links, link => Assert.True(link.Strength <= 2));
    }

    [Fact]
    public void Bind_TestCaseProjectMismatch_IsRejected()
    {
        var plan = new ProofPlan([
            new ProofObligation(
                "O1",
                "P004",
                ObligationKind.Test,
                "claim",
                "App.Tests.OrderServiceTests.Cancel_keeps_order",
                true,
                4,
                ["r"],
                new ProofSubject(
                    SubjectKind.Test,
                    "App.Tests.OrderServiceTests.Cancel_keeps_order",
                    "App.Tests",
                    DisplayName: "App.Tests.OrderServiceTests.Cancel_keeps_order"))
        ]);
        var evidence = new[]
        {
            Evidence(
                "E1",
                EvidenceKind.TestCase,
                EvidenceStatus.Pass,
                "unit",
                checkId: "unit",
                subjectRefs:
                [
                    new EvidenceSubjectRef(
                        SubjectKind.Test,
                        "App.Tests.OrderServiceTests.Cancel_keeps_order",
                        FullyQualifiedName: "App.Tests.OrderServiceTests.Cancel_keeps_order",
                        Project: "Other.Tests")
                ])
        };

        var bound = new EvidenceBinder().Bind(plan, evidence);
        Assert.DoesNotContain(bound.Links, link => link.ObligationId == "O1" && link.Relation == "direct");
    }

    [Fact]
    public void Bind_TestCaseTrimmedSignatureEquality_IsDirect()
    {
        var plan = new ProofPlan([
            Obligation("O1", ObligationKind.Test, "sym-1", displayName: "App.Tests.OrderServiceTests.Cancel_keeps_order")
        ]);
        var evidence = new[]
        {
            Evidence(
                "E1",
                EvidenceKind.TestCase,
                EvidenceStatus.Pass,
                "unit",
                checkId: "unit",
                subjectRefs:
                [
                    new EvidenceSubjectRef(
                        SubjectKind.Test,
                        "App.Tests.OrderServiceTests.Cancel_keeps_order",
                        FullyQualifiedName: "App.Tests.OrderServiceTests.Cancel_keeps_order(System.String)",
                        Project: "App.Tests")
                ])
        };

        var bound = new EvidenceBinder().Bind(plan, evidence);
        Assert.Contains(bound.Links, link => link.ObligationId == "O1" && link.Relation == "direct");
    }

    private static ProofObligation Obligation(
        string id,
        ObligationKind kind,
        string subjectId,
        string? displayName = null)
        => new(
            id,
            "P000",
            kind,
            $"claim-{id}",
            subjectId,
            true,
            1,
            ["test"],
            new ProofSubject(kind == ObligationKind.Test ? SubjectKind.Test : SubjectKind.Symbol, subjectId, DisplayName: displayName));

    private static ProofEvidence Evidence(
        string id,
        EvidenceKind kind,
        EvidenceStatus status,
        string subject,
        string? commandTarget = null,
        IReadOnlyList<string>? scopeSubjects = null,
        string? checkId = null,
        string? sourceDigest = null,
        IReadOnlyList<EvidenceSubjectRef>? subjectRefs = null)
        => new(
            id,
            kind,
            subject,
            status,
            new EvidenceProvenance("distill", CheckId: checkId ?? subject, SourceDigest: sourceDigest),
            commandTarget is null && scopeSubjects is null && subjectRefs is null
                ? null
                : new EvidenceScope(
                    commandTarget is not null
                    && (commandTarget.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                        || commandTarget.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
                        ? ScopeMode.RepositoryWide
                        : subjectRefs is { Count: > 0 } ? ScopeMode.Exact : ScopeMode.Contains,
                    scopeSubjects,
                    commandTarget,
                    subjectRefs));
}

public sealed class DeterministicProofEvaluatorInvariantTests
{
    [Fact]
    public void Evaluate_NonEmptyChangeWithEmptyPlan_IsUncertain()
    {
        var plan = new ProofPlan([], ChangeSetIsEmpty: false);
        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, new VerificationEvidenceSet([], []));
        Assert.Equal(ProofVerdict.Uncertain, evaluation.Verdict);
        Assert.Equal(ProofReasonCodes.ChangeNonemptyPlanEmpty, evaluation.ReasonCode);
    }

    [Fact]
    public void Evaluate_EmptyChange_IsNoChange()
    {
        var plan = new ProofPlan([], ChangeSetIsEmpty: true);
        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, new VerificationEvidenceSet([], []));
        Assert.Equal(ProofVerdict.NoChange, evaluation.Verdict);
    }

    [Fact]
    public void Evaluate_UnrelatedPassCannotImproveMissingRequired()
    {
        var plan = new ProofPlan([
            new ProofObligation("O1", "P004", ObligationKind.Test, "claim", "TestA", true, 4, ["r"])
        ]);
        var without = new VerificationEvidenceSet([], []);
        var withUnrelated = new VerificationEvidenceSet(
            [
                new ProofEvidence(
                    "E1",
                    EvidenceKind.TestRun,
                    "other",
                    EvidenceStatus.Pass,
                    new EvidenceProvenance("distill"),
                    new EvidenceScope(Subjects: ["TestB"]))
            ],
            []);

        var left = new DeterministicProofEvaluator().Evaluate(plan, without);
        var right = new DeterministicProofEvaluator().Evaluate(plan, withUnrelated);
        Assert.Equal(left.Verdict, right.Verdict);
        Assert.Equal(left.Obligations[0].Status, right.Obligations[0].Status);
    }
}
