using Proof.Adapters.CodeMap;
using Proof.Adapters.Distill;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

public sealed class ApiCompatBindingTests
{
    [Fact]
    public void Bind_SolutionWideApiCompat_IsSupportingOnly()
    {
        var obligation = new ProofObligation(
            "O1",
            "P001A",
            ObligationKind.Compatibility,
            "api",
            "sym-1",
            true,
            4,
            ["r"],
            new ProofSubject(SubjectKind.ApiSurface, "sym-1", "Proof.Core"));
        var plan = new ProofPlan([obligation], SourceDigest: "src");
        var evidence = new[]
        {
            new ProofEvidence(
                "E1",
                EvidenceKind.ApiCompatibility,
                "apicompat",
                EvidenceStatus.Pass,
                new EvidenceProvenance("distill", SourceDigest: "src", CheckId: "apicompat"),
                new EvidenceScope(
                    ScopeMode.RepositoryWide,
                    CommandTarget: "Proof.slnx",
                    SubjectRefs:
                    [
                        new EvidenceSubjectRef(SubjectKind.Project, "Proof.Engine", "Proof.Engine")
                    ]))
        };

        var bound = new EvidenceBinder().Bind(
            plan,
            evidence,
            new VerificationPlan([new PlannedVerificationCheck("apicompat", "apicompatibility", string.Empty)], [], "full"));
        Assert.Contains(bound.Links, link => link.Relation == "supporting" && link.BindingRuleId == "BIND_API_COMPAT_SUPPORTING");
        Assert.DoesNotContain(bound.Links, link => link.Relation == "direct");
    }

    [Fact]
    public void Bind_ProjectScopedDistillFail_OverridesFingerprintPass()
    {
        var obligation = new ProofObligation(
            "O1",
            "P001A",
            ObligationKind.Compatibility,
            "api",
            "sym-1",
            true,
            4,
            ["r"],
            new ProofSubject(SubjectKind.ApiSurface, "sym-1", "App"));
        var plan = new ProofPlan([obligation], SourceDigest: "src");
        var fingerprint = new ProofEvidence(
            "FP",
            EvidenceKind.ApiCompatibility,
            "sym-1",
            EvidenceStatus.Pass,
            new EvidenceProvenance("codemap", SourceDigest: "src", CheckId: "api-compatibility"),
            new EvidenceScope(
                ScopeMode.Exact,
                ["sym-1"],
                "App",
                [
                    new EvidenceSubjectRef(SubjectKind.ApiSurface, "sym-1", "App", DisplayName: "Lib.Foo")
                ]));
        var distillFail = new ProofEvidence(
            "DC",
            EvidenceKind.ApiCompatibility,
            "apicompat",
            EvidenceStatus.Fail,
            new EvidenceProvenance("distill", SourceDigest: "src", CheckId: "apicompat"),
            new EvidenceScope(
                ScopeMode.Exact,
                ["App"],
                "App/App.csproj",
                [new EvidenceSubjectRef(SubjectKind.Project, "App", "App")]));

        var bound = new EvidenceBinder().Bind(
            plan,
            [fingerprint, distillFail],
            new VerificationPlan(
                [
                    new PlannedVerificationCheck("api-compatibility", "apicompatibility", string.Empty),
                    new PlannedVerificationCheck("apicompat", "apicompatibility", string.Empty)
                ],
                [],
                "full"));
        var evaluation = new DeterministicProofEvaluator().Evaluate(plan, bound);
        Assert.Equal(ProofVerdict.NotReady, evaluation.Verdict);
        Assert.Equal(ObligationStatus.Failed, evaluation.Obligations[0].Status);
    }

    [Fact]
    public void ResolveApiCompatProjectRefs_UsesDiagnostics()
    {
        var diagnostics = new[]
        {
            Distill.Core.Diagnostics.DistillDiagnostic.Create(
                "a",
                Distill.Core.Diagnostics.DiagnosticKind.Analysis,
                Distill.Core.Diagnostics.DiagnosticSeverity.Info,
                "apicompat",
                "PASS",
                "ok",
                project: "Proof.Core")
        };
        var refs = DistillVerificationRunner.ResolveApiCompatProjectRefs(null, diagnostics);
        Assert.Contains(refs, reference => reference.Project == "Proof.Core");
    }
}
