using Proof.Core;

namespace Proof.Engine;

public sealed class ChangeCertificateBuilder : IChangeCertificateBuilder
{
    // 압축 인증서 사이드카(Wave B step 6). ComputeStatementDigest에서
    // 의도적으로 뺀다. 요약은 파생 출력이며, 서명된 statement
    // 페이로드의 일부가 결코 아니다.
    public static CertificateSummary ToSummary(ChangeCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        var unresolvedReasons = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var evaluated in certificate.Evaluation.Obligations.Where(item => item.Status != ObligationStatus.Proven))
        {
            unresolvedReasons[evaluated.Obligation.Id] = ResolveSummaryReasonCode(certificate, evaluated.Obligation);
        }

        return new CertificateSummary(
            certificate.Verdict,
            certificate.Evaluation.ReasonCode,
            certificate.SourceDigest,
            certificate.CertificateDigest,
            certificate.StatementDigest,
            certificate.Evaluation.Obligations.Count(item => item.Status == ObligationStatus.Proven),
            unresolvedReasons.Count,
            [.. certificate.Evaluation.Obligations
                .Where(item => item.Status != ObligationStatus.Proven)
                .Select(item => new SummaryUnresolvedObligation(
                    item.Obligation.RuleId,
                    item.Obligation.Claim,
                    item.Status,
                    unresolvedReasons.GetValueOrDefault(item.Obligation.Id),
                    item.Obligation.SubjectId,
                    item.Obligation.Subject?.File))
                .OrderBy(item => item.RuleId, StringComparer.Ordinal)
                .ThenBy(item => item.Claim, StringComparer.Ordinal)],
            [.. (certificate.Constraints ?? [])
                .Where(item => string.Equals(item.Severity, "blocking", StringComparison.OrdinalIgnoreCase))
                .Select(item => new SummaryConstraint(item.Code, item.Message, item.Subject))]);
    }

    private static string? ResolveSummaryReasonCode(ChangeCertificate certificate, ProofObligation obligation)
    {
        var constraint = (certificate.Constraints ?? [])
            .Where(item => string.Equals(item.Severity, "blocking", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(item => string.Equals(item.Subject, obligation.Id, StringComparison.Ordinal));
        if (constraint is not null)
        {
            return constraint.Code;
        }

        return certificate.Evaluation.ReasonCode
               ?? obligation.Reasons.FirstOrDefault()
               ?? ProofReasonCodes.RequiredEvidenceMissing;
    }

    public ChangeCertificate Build(
        ChangeImpact impact,
        ProofPlan plan,
        VerificationEvidenceSet evidence,
        ProofEvaluation evaluation,
        CertificateMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(impact);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(evaluation);

        var hashedEvidence = evidence.Evidence.Select(item =>
        {
            var pointer = item.Provenance.ArtifactPointer;
            var logical = RepositoryPathNormalizer.Normalize(pointer, metadata?.WorkspaceRoot);
            return item with
            {
                Provenance = item.Provenance with
                {
                    ArtifactPointer = string.IsNullOrWhiteSpace(logical) ? pointer : logical
                }
            };
        }).ToArray();

        var evidenceSet = evidence with { Evidence = hashedEvidence };
        var runId = metadata?.RunId ?? RunIdFactory.Create(impact, plan, metadata?.VerificationPlan);
        var verificationPlan = metadata?.VerificationPlan;
        var manifest = hashedEvidence
            .Where(item => !string.IsNullOrWhiteSpace(item.Provenance.Sha256))
            .Select(item => new EvidenceArtifactManifestEntry(
                item.Id,
                RepositoryPathNormalizer.Normalize(item.Provenance.ArtifactPointer, metadata?.WorkspaceRoot),
                item.Provenance.Sha256!))
            .ToArray();

        var unsigned = new ChangeCertificate(
            SchemaVersion: 3,
            BaseRevision: impact.BaseRevision,
            HeadRevision: impact.HeadRevision,
            Verdict: evaluation.Verdict,
            Impact: impact,
            Plan: plan,
            Evidence: evidenceSet,
            Evaluation: evaluation,
            RunId: runId,
            SourceDigest: impact.SourceDigest ?? plan.SourceDigest,
            IsDirty: metadata?.IsDirty ?? false,
            Toolchain: new ToolchainIdentity(
                CertificateCanonicalHasher.AssemblyVersion(typeof(ChangeCertificateBuilder)),
                metadata?.CodeMapVersion,
                metadata?.DistillVersion,
                metadata?.ProofConfigDigest,
                metadata?.DistillConfigDigest,
                metadata?.ProofBinarySha256,
                metadata?.CodeMapBinarySha256,
                metadata?.DistillBinarySha256),
            VerificationPlan: verificationPlan,
            Constraints: plan.Constraints,
            CertificateDigest: null,
            ArtifactManifest: manifest);

        var statementDigest = CertificateCanonicalHasher.ComputeStatementDigest(unsigned);
        unsigned = unsigned with { StatementDigest = statementDigest };
        return unsigned with { CertificateDigest = CertificateCanonicalHasher.ComputeDigest(unsigned) };
    }

    internal static string CreateRunId(ChangeImpact impact, ProofPlan plan, VerificationPlan? verificationPlan)
        => RunIdFactory.Create(impact, plan, verificationPlan);

    internal static string NormalizeLogicalPath(string? path, string? workspaceRoot = null)
        => RepositoryPathNormalizer.Normalize(path, workspaceRoot);
}
