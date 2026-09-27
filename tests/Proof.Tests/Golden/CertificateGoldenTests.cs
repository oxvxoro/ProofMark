using FsCheck;
using FsCheck.Xunit;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests.Golden;

/// <summary>
/// 골든 시나리오에서 인증서의 서명된 식별을 고정한다. 같은 입력으로
/// 반복한 빌드는 동일한 statement/certificate
/// 다이제스트와 아티팩트 매니페스트를 만들어야 하고, 차단하는 불확실성을 더해도
/// 판정을 절대 강화하면 안 된다.
/// </summary>
public sealed class CertificateGoldenTests
{
    [Fact]
    public void Build_GoldenScenario_ProducesStableSignedIdentity()
    {
        var first = GoldenScenarioFactory.BuildCertificate();
        var second = GoldenScenarioFactory.BuildCertificate();

        Assert.Equal(first.Verdict, second.Verdict);
        Assert.Equal(first.StatementDigest, second.StatementDigest);
        Assert.Equal(first.CertificateDigest, second.CertificateDigest);
        Assert.Equal(Manifest(first), Manifest(second));
    }

    [Property]
    public bool AddingBlockingUncertainty_NeverStrengthensVerdict(PositiveInt seed)
    {
        var plan = GoldenScenarioFactory.Plan();
        var bound = new EvidenceBinder().Bind(plan, GoldenScenarioFactory.Evidence());
        var withBlocking = plan with
        {
            Constraints =
            [
                .. plan.Constraints ?? [],
                new AnalysisConstraint(
                    $"C-{seed.Get}",
                    ProofReasonCodes.ImpactPotentiallyTruncated,
                    "blocking",
                    "impact traversal reached a result limit",
                    null)
            ]
        };

        var before = new DeterministicProofEvaluator().Evaluate(plan, bound).Verdict;
        var after = new DeterministicProofEvaluator().Evaluate(withBlocking, bound).Verdict;
        return Strength(after) <= Strength(before);
    }

    private static int Strength(ProofVerdict verdict) => verdict switch
    {
        ProofVerdict.Proven or ProofVerdict.NoChange => 3,
        ProofVerdict.Uncertain => 2,
        ProofVerdict.NotReady => 1,
        _ => 0
    };

    private static string Manifest(ChangeCertificate certificate)
        => string.Join(
            '\n',
            (certificate.ArtifactManifest ?? [])
                .Select(entry => $"{entry.LogicalId}|{entry.RelativePath}|{entry.Sha256}"));
}
