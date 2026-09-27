using System.CommandLine;
using System.Text.Json;
using Proof.Core;
using Proof.Engine;

namespace Proof.Cli;

public static class ExplainCommand
{
    public static Command Create()
    {
        var fileArgument = new Argument<string?>("certificate")
        {
            Description = "Certificate JSON path. Defaults to the latest .proof/certificates entry.",
            DefaultValueFactory = _ => null
        };
        var obligationOption = new Option<string?>("--obligation")
        {
            Description = "Explain a single obligation id or subject.",
            DefaultValueFactory = _ => null
        };
        var command = new Command("explain", "Explain why obligations are Unresolved without re-running the engine.")
        {
            fileArgument,
            obligationOption
        };
        command.SetAction((parseResult, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Execute(
                parseResult.GetValue(fileArgument),
                parseResult.GetValue(obligationOption)));
        });
        return command;
    }

    internal static int Execute(string? certificatePath, string? obligationFilter)
    {
        var certificate = CertificateExplainer.LoadCertificate(Directory.GetCurrentDirectory(), certificatePath, out var error);
        if (certificate is null)
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        var explanations = CertificateExplainer.Explain(certificate, obligationFilter);
        if (explanations.Count == 0)
        {
            Console.WriteLine("No matching obligation.");
            return 0;
        }

        foreach (var explanation in explanations)
        {
            Console.WriteLine($"Obligation: {explanation.ObligationId}  ({explanation.RuleId})  status={explanation.Status}");
            Console.WriteLine($"  Claim: {explanation.Claim}");
            if (!string.IsNullOrWhiteSpace(explanation.Reason))
            {
                Console.WriteLine($"  Reason: {explanation.Reason}");
            }

            foreach (var link in explanation.Links)
            {
                Console.WriteLine(
                    $"  Link: {link.EvidenceId} {link.Kind}/{link.Status} check={link.CheckId} "
                    + $"{link.Relation} strength={link.Strength} rule={link.BindingRuleId ?? "-"} "
                    + $"code={link.ReasonCode ?? "-"}");
                if (!string.IsNullOrWhiteSpace(link.Explanation))
                {
                    Console.WriteLine($"        {link.Explanation}");
                }
            }

            foreach (var constraint in explanation.Constraints)
            {
                Console.WriteLine($"  Constraint: [{constraint.Severity}] {constraint.Code}  {constraint.Message}");
            }

            if (!string.IsNullOrWhiteSpace(explanation.NextStep))
            {
                Console.WriteLine($"  Next step: {explanation.NextStep}");
            }
        }

        return 0;
    }
}

public static class ObligationsCommand
{
    public static Command Create()
    {
        var openOption = new Option<bool>("--open")
        {
            Description = "Only show Unresolved or Failed obligations.",
            DefaultValueFactory = _ => false
        };
        var certificateOption = new Option<string?>("--certificate")
        {
            Description = "Certificate JSON path. Defaults to the latest .proof/certificates entry.",
            DefaultValueFactory = _ => null
        };
        var command = new Command("obligations", "List certificate obligations (read-only query).")
        {
            openOption,
            certificateOption
        };
        command.SetAction((parseResult, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Execute(parseResult.GetValue(openOption), parseResult.GetValue(certificateOption)));
        });
        return command;
    }

    internal static int Execute(bool openOnly, string? certificatePath)
    {
        var certificate = CertificateExplainer.LoadCertificate(Directory.GetCurrentDirectory(), certificatePath, out var error);
        if (certificate is null)
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        var obligations = certificate.Evaluation.Obligations
            .Where(item => !openOnly || item.Status is ObligationStatus.Unresolved or ObligationStatus.Failed)
            .ToArray();

        Console.WriteLine(openOnly ? $"OPEN ({obligations.Length})" : $"OBLIGATIONS ({obligations.Length})");
        foreach (var item in obligations)
        {
            Console.WriteLine(
                $"  {item.Status,-11} {item.Obligation.RuleId,-5} {item.Obligation.SubjectId}  {item.Obligation.Claim}");
        }

        return 0;
    }
}

/// <summary>읽기 전용 인증서 검사. 파싱과 투영만 한다.</summary>
internal static class CertificateExplainer
{
    public static ChangeCertificate? LoadCertificate(string workspaceRoot, string? path, out string? error)
    {
        var resolved = string.IsNullOrWhiteSpace(path) ? LatestCertificate(workspaceRoot) : path;
        if (string.IsNullOrWhiteSpace(resolved) || !File.Exists(resolved))
        {
            error = $"Certificate not found: {resolved ?? "(none)"}";
            return null;
        }

        try
        {
            var certificate = JsonSerializer.Deserialize<ChangeCertificate>(File.ReadAllText(resolved), ProofJson.WireOptions);
            if (certificate is null)
            {
                error = $"Certificate deserialized to null: {resolved}";
                return null;
            }

            error = null;
            return certificate;
        }
        catch (JsonException exception)
        {
            error = $"Invalid certificate JSON: {exception.Message}";
            return null;
        }
    }

    public static string? LatestCertificate(string workspaceRoot)
    {
        var directory = Path.Combine(workspaceRoot, ".proof", "certificates");
        if (!Directory.Exists(directory))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
            .Where(VerifyCommand.IsCertificateFile)
            .OrderBy(path => path, StringComparer.Ordinal)
            .LastOrDefault();
    }

    public static IReadOnlyList<ObligationExplanation> Explain(ChangeCertificate certificate, string? obligationFilter)
    {
        var evidenceById = certificate.Evidence.Evidence.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var result = new List<ObligationExplanation>();
        foreach (var evaluated in certificate.Evaluation.Obligations)
        {
            if (!string.IsNullOrWhiteSpace(obligationFilter)
                && !string.Equals(evaluated.Obligation.Id, obligationFilter, StringComparison.Ordinal)
                && !string.Equals(evaluated.Obligation.SubjectId, obligationFilter, StringComparison.Ordinal))
            {
                continue;
            }

            var links = certificate.Evidence.Links
                .Where(link => string.Equals(link.ObligationId, evaluated.Obligation.Id, StringComparison.Ordinal))
                .OrderBy(link => link.EvidenceId, StringComparer.Ordinal)
                .Select(link =>
                {
                    evidenceById.TryGetValue(link.EvidenceId, out var evidence);
                    return new EvidenceLinkExplanation(
                        link.EvidenceId,
                        evidence?.Kind ?? EvidenceKind.Build,
                        evidence?.Status ?? EvidenceStatus.Inconclusive,
                        evidence?.Provenance.CheckId ?? string.Empty,
                        link.Relation,
                        link.Strength,
                        link.BindingRuleId,
                        link.ReasonCode,
                        link.Explanation);
                })
                .ToArray();

            var constraints = (certificate.Constraints ?? [])
                .Concat(certificate.Plan.Constraints ?? [])
                .Where(constraint =>
                    string.Equals(constraint.Subject, evaluated.Obligation.Id, StringComparison.Ordinal)
                    || string.Equals(constraint.Subject, evaluated.Obligation.SubjectId, StringComparison.Ordinal))
                .DistinctBy(constraint => constraint.Id, StringComparer.Ordinal)
                .OrderBy(constraint => constraint.Code, StringComparer.Ordinal)
                .ToArray();

            result.Add(new ObligationExplanation(
                evaluated.Obligation.Id,
                evaluated.Obligation.RuleId,
                evaluated.Obligation.Claim,
                evaluated.Status,
                evaluated.Obligation.Reasons.FirstOrDefault(),
                links,
                constraints,
                NextStep(evaluated.Obligation, evaluated.Status)));
        }

        return result;
    }

    // 결정적 작성 힌트만이다. 증거로 결코 파싱되지 않는다.
    private static string? NextStep(ProofObligation obligation, ObligationStatus status)
    {
        if (status == ObligationStatus.Proven)
        {
            return null;
        }

        var subject = obligation.Subject?.DisplayName ?? obligation.SubjectId;
        return obligation.RuleId switch
        {
            "P005" => $"close it explicitly: 'proof map suggest' for heuristic candidates, then "
                      + $"proof map add --symbol \"{subject}\" --test <dotted test FQN> --accept "
                      + "(or run the mapped tests under coverage and use 'proof map from-coverage')",
            "P001A" when status is ObligationStatus.Unresolved or ObligationStatus.Blocked =>
                "re-run with a CodeMap baseline index (commit or stash changes, verify once, then retry) "
                + "or add an opt-in PackageValidation apicompatibility check in distill.yml (full profile) "
                + "so public API evidence is not Inconclusive.",
            "P008" when status == ObligationStatus.Failed =>
                "fix analyzer diagnostics for the obligated project, then re-run `proof verify` "
                + "(see distill `analyzers` check / SARIF artifacts under .distill/runs).",
            "P009" when status == ObligationStatus.Unresolved =>
                $"sign a manual review: proof review sign --subject \"{obligation.SubjectId}\" "
                + "(requires PROOF_ATTESTATION_HMAC_KEY; review JSON stays under .proof/reviews/).",
            "P011" when status == ObligationStatus.Unresolved =>
                obligation.SubjectId.Contains("architecture-rules", StringComparison.OrdinalIgnoreCase)
                    ? "add `.codemap/architecture.json` matching your layer names, then re-run `proof verify`."
                    : "fix the reported architecture violation or adjust `.codemap/architecture.json` forbid rules, then re-run `proof verify`.",
            "P002" or "P004" =>
                "run the mapped or impacted test project via `proof verify` (Distill test host) "
                + "and confirm the test case evidence binds to this obligation.",
            _ => null
        };
    }

    public static CertificateDiff Diff(ChangeCertificate left, ChangeCertificate right)
    {
        var leftObligations = left.Evaluation.Obligations.Select(item => item.Obligation.Id).ToHashSet(StringComparer.Ordinal);
        var rightObligations = right.Evaluation.Obligations.Select(item => item.Obligation.Id).ToHashSet(StringComparer.Ordinal);
        var leftEvidence = left.Evidence.Evidence.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var rightEvidence = right.Evidence.Evidence.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);

        return new CertificateDiff(
            left.Verdict,
            right.Verdict,
            left.StatementDigest,
            right.StatementDigest,
            left.CertificateDigest,
            right.CertificateDigest,
            leftObligations.Except(rightObligations, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            rightObligations.Except(leftObligations, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            leftEvidence.Except(rightEvidence, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            rightEvidence.Except(leftEvidence, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray());
    }
}

internal sealed record ObligationExplanation(
    string ObligationId,
    string RuleId,
    string Claim,
    ObligationStatus Status,
    string? Reason,
    IReadOnlyList<EvidenceLinkExplanation> Links,
    IReadOnlyList<AnalysisConstraint> Constraints,
    string? NextStep = null);

internal sealed record EvidenceLinkExplanation(
    string EvidenceId,
    EvidenceKind Kind,
    EvidenceStatus Status,
    string CheckId,
    string Relation,
    int Strength,
    string? BindingRuleId,
    string? ReasonCode,
    string? Explanation);

internal sealed record CertificateDiff(
    ProofVerdict LeftVerdict,
    ProofVerdict RightVerdict,
    string? LeftStatementDigest,
    string? RightStatementDigest,
    string? LeftCertificateDigest,
    string? RightCertificateDigest,
    IReadOnlyList<string> ObligationsOnlyInLeft,
    IReadOnlyList<string> ObligationsOnlyInRight,
    IReadOnlyList<string> EvidenceOnlyInLeft,
    IReadOnlyList<string> EvidenceOnlyInRight);