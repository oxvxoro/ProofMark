using System.CommandLine;
using System.Text.Json;
using Proof.Adapters.CodeMap;
using Proof.Adapters.Distill;
using Proof.Adapters.Git;
using Proof.Core;
using Proof.Engine;

namespace Proof.Cli;

public static class VerifyCommand
{
    public static Command Create()
    {
        var baseOption = new Option<string?>("--base")
        {
            Description = "Git revision to compare against."
        };

        var outputOption = new Option<string>("--output")
        {
            Description = "Output format: compact or json.",
            DefaultValueFactory = _ => "compact"
        };

        var profileOption = new Option<string?>("--profile")
        {
            Description = "Optional Distill profile override (e.g. full). Defaults to proof.yml.",
            DefaultValueFactory = _ => (string?)null
        };

        var command = new Command("verify", "Verify a change against semantic impact and Distill evidence.")
        {
            baseOption,
            outputOption,
            profileOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var baseRevision = parseResult.GetValue(baseOption);
            var output = parseResult.GetValue(outputOption)!;
            var profile = parseResult.GetValue(profileOption);
            return await ExecuteAsync(baseRevision, output, profile, cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    internal static async Task<int> ExecuteAsync(
        string? baseRevision,
        string output,
        string? profile,
        CancellationToken cancellationToken)
    {
        var workspaceRoot = Directory.GetCurrentDirectory();
        ProofConfig config;
        try
        {
            config = ProofConfig.Load(workspaceRoot);
        }
        catch (ProofConfigException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }

        string effectiveBase;
        string headSha;
        ProofRuntimeContext runtime;
        var metrics = new ProofMetricsCollector();
        try
        {
            (effectiveBase, headSha) = await ProofWorkspace.ResolveBaseAsync(workspaceRoot, config, baseRevision, cancellationToken).ConfigureAwait(false);
            runtime = ProofWorkspace.CreateRuntime(workspaceRoot, config, metrics);
        }
        catch (ProofConfigException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }

        var usedFallback = false;
        var distillConfigPath = runtime.DistillConfigPath;
        var orchestrator = runtime.Orchestrator;

        ChangeCertificate certificate;
        try
        {
            var proofConfigPath = ProofConfig.ResolveConfigPath(workspaceRoot);
            certificate = await orchestrator.VerifyFromWorkspaceAsync(
                workspaceRoot,
                effectiveBase,
                headSha,
                profile ?? config.Verification.DistillProfile,
                cancellationToken,
                config.ToPolicy(),
                DistillVerificationRunner.MergeProducerCapabilities(
                    DistillVerificationRunner.BuildCatalog(workspaceRoot, distillConfigPath, profile ?? config.Verification.DistillProfile),
                    profile ?? config.Verification.DistillProfile),
                new CertificateMetadata(
                    ProofConfigDigest: File.Exists(proofConfigPath) ? CertificateCanonicalHasher.HashFile(proofConfigPath) : null,
                    DistillConfigDigest: File.Exists(distillConfigPath) ? CertificateCanonicalHasher.HashFile(distillConfigPath) : null,
                    CodeMapVersion: CertificateCanonicalHasher.AssemblyVersion(typeof(CodeMapChangeImpactProvider)),
                    DistillVersion: CertificateCanonicalHasher.AssemblyVersion(typeof(DistillVerificationRunner)),
                    WorkspaceRoot: workspaceRoot,
                    ProofBinarySha256: CertificateCanonicalHasher.AssemblyFileSha256(System.Reflection.Assembly.GetEntryAssembly()),
                    CodeMapBinarySha256: CertificateCanonicalHasher.AssemblyFileSha256(typeof(CodeMap.Storage.IncrementalCodeMapIndexer).Assembly),
                    DistillBinarySha256: CertificateCanonicalHasher.AssemblyFileSha256(typeof(Distill.Execution.DistillCheckExecutor).Assembly))).ConfigureAwait(false);
            usedFallback = certificate.Impact.UsedFileWideFallback;
        }
        catch (ProofConfigException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Proof verification failed: {exception.Message}");
            return 2;
        }

        var certificatePath = WriteCertificate(workspaceRoot, certificate);
        var summaryPath = WriteSummary(workspaceRoot, certificate, Path.GetFileNameWithoutExtension(certificatePath));
        WriteMetrics(workspaceRoot, certificate.RunId, metrics.Last);
        if (string.Equals(output, "json", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(JsonSerializer.Serialize(certificate, ProofJson.WireOptions));
        }
        else
        {
            WriteCompact(certificate, certificatePath, usedFallback);
            Console.WriteLine($"Summary: {summaryPath}");
        }

        return certificate.Verdict switch
        {
            ProofVerdict.Proven or ProofVerdict.NoChange => 0,
            ProofVerdict.NotReady => 1,
            ProofVerdict.Uncertain => ShouldFailCi(certificate, config.ToPolicy()) ? 1 : 3,
            ProofVerdict.InfraError => 4,
            _ => 2
        };
    }

    // CI merge-block(Wave B step 6). 나열된 이유 코드가 이끄는 Uncertain
    // 판정은 조언으로 남지 않고 실행을 실패시킨다(exit 1).
    // *필수* 의무만 merge-block에 넣는다. 조언 의무
    // (예: Required: false인 범위 밖 P005)가 혼자
    // exit 1로 뒤집어서는 안 된다.
    internal static bool ShouldFailCi(ChangeCertificate certificate, ProofPolicy policy)
    {
        var codes = policy.FailOnUncertainCodes;
        if (codes is not { Count: > 0 } || certificate.Verdict != ProofVerdict.Uncertain)
        {
            return false;
        }

        var blocked = certificate.Evaluation.Obligations
            .Where(item => item.Obligation.Required && item.Status != ObligationStatus.Proven)
            .Select(item => ResolveUnresolvedReasonCode(certificate, item.Obligation))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var code in codes)
        {
            if (string.Equals(certificate.Evaluation.ReasonCode, code, StringComparison.Ordinal))
            {
                return true;
            }

            if ((certificate.Constraints ?? []).Concat(certificate.Plan.Constraints ?? []).Any(item =>
                    string.Equals(item.Code, code, StringComparison.Ordinal)
                    && string.Equals(item.Severity, "blocking", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (blocked.Contains(code))
            {
                return true;
            }
        }

        return false;
    }

    // merge-block된 의무를 이끄는 이유 코드를 해석한다.
    // blocking constraint는 실제 코드를 담는다. 의무 이유도
    // 플래너 산문(예: "no mapped test relation found")을 담으므로, 이유는
    // ProofReasonCodes가 알 때만 코드로 친다.
    // REQUIRED_EVIDENCE_MISSING 폴백은 테스트 매핑 의무
    // (P005)로 한정되어 merge-block이 문서화된 의미와 맞는다. 이유가
    // 플래너 산문뿐인 필수 비매핑 의무(예: 바인딩되지 않은
    // P004 영향 테스트)는 새로 merge-block하지 않고
    // 기존의 조언 취급을 유지한다.
    private static string ResolveUnresolvedReasonCode(ChangeCertificate certificate, ProofObligation obligation)
    {
        var constraint = (certificate.Constraints ?? [])
            .Where(item => string.Equals(item.Severity, "blocking", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(item => string.Equals(item.Subject, obligation.Id, StringComparison.Ordinal));
        if (constraint is not null)
        {
            return constraint.Code;
        }

        return certificate.Evaluation.ReasonCode
               ?? obligation.Reasons.FirstOrDefault(ProofReasonCodes.IsKnown)
               ?? (obligation.Kind == ObligationKind.TestMapping
                   ? ProofReasonCodes.RequiredEvidenceMissing
                   : obligation.Reasons.FirstOrDefault() ?? ProofReasonCodes.RequiredEvidenceMissing);
    }

    // 큰 변경이 터미널을 채우지 않도록 목록을 제한한다. 전체 집합은
// 항상 인증서와 요약 사이드카에 있다.
    internal const int OpenP005PreviewCount = 10;

    private static void WriteOpenP005(ChangeCertificate certificate)
    {
        var subjects = certificate.Evaluation.Obligations
            .Where(item => item.Obligation.RuleId == "P005" && item.Status != ObligationStatus.Proven)
            .Select(item => item.Obligation.Subject?.DisplayName ?? item.Obligation.SubjectId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (subjects.Length == 0)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"Open P005 ({subjects.Length}):");
        foreach (var subject in subjects.Take(OpenP005PreviewCount))
        {
            Console.WriteLine($"  {subject}");
        }

        if (subjects.Length > OpenP005PreviewCount)
        {
            Console.WriteLine($"  ... and {subjects.Length - OpenP005PreviewCount} more");
        }
    }

    internal static bool IsCertificateFile(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
               && !name.EndsWith(".summary.json", StringComparison.OrdinalIgnoreCase);
    }

    private static string WriteCertificate(string workspaceRoot, ChangeCertificate certificate)
    {
        var directory = Path.Combine(workspaceRoot, ".proof", "certificates");
        Directory.CreateDirectory(directory);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var path = Path.Combine(directory, $"{stamp}-{certificate.RunId ?? "certificate"}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(certificate, ProofJson.WireOptions));
        return path;
    }

    private static string WriteSummary(string workspaceRoot, ChangeCertificate certificate, string certificateStamp)
    {
        var directory = Path.Combine(workspaceRoot, ".proof", "certificates");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{certificateStamp}.summary.json");
        File.WriteAllText(path, JsonSerializer.Serialize(ChangeCertificateBuilder.ToSummary(certificate), ProofJson.WireOptions));
        return path;
    }

    // 진단 사이드카(PR-13). 한 실행의 단계 시간과 개수다. 인증서
    // 페이로드나 서명된 statement의 일부가 아니다.
    internal static string? WriteMetrics(string workspaceRoot, string? runId, VerificationMetrics? metrics)
    {
        if (metrics is null || string.IsNullOrWhiteSpace(runId))
        {
            return null;
        }

        var directory = Path.Combine(workspaceRoot, ".proof", "runs", runId);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "metrics.json");
        File.WriteAllText(path, JsonSerializer.Serialize(metrics, ProofJson.WireOptions));
        return path;
    }

    private static void WriteCompact(ChangeCertificate certificate, string certificatePath, bool usedFallback)
    {
        Console.WriteLine("CHANGE PROOF");
        Console.WriteLine();
        Console.WriteLine($"Base: {certificate.BaseRevision}");
        Console.WriteLine($"Head: {certificate.HeadRevision}");
        Console.WriteLine($"Source: {certificate.SourceDigest}");
        Console.WriteLine($"Changed: {certificate.Impact.ChangedSymbols.Count} symbols / {certificate.Impact.Spans.Count} spans");
        Console.WriteLine($"Impact: {certificate.Impact.ImpactedSymbols.Count} symbols / {certificate.Impact.ImpactedProjects.Count} projects");
        if (usedFallback)
        {
            Console.WriteLine("Note: file-wide span fallback was used because Git hunks were empty.");
        }

        Console.WriteLine();

        var proven = certificate.Evaluation.Obligations.Count(item => item.Status == ObligationStatus.Proven);
        var unresolved = certificate.Evaluation.Obligations.Count(item =>
            item.Status is ObligationStatus.Unresolved or ObligationStatus.Blocked or ObligationStatus.Pending);
        Console.WriteLine($"Obligations: {proven} proven / {unresolved} unresolved");
        Console.WriteLine();

        foreach (var item in certificate.Evaluation.Obligations.Where(item => item.Status != ObligationStatus.Proven))
        {
            var reasonCode = ResolveUnresolvedReasonCode(certificate, item.Obligation);
            Console.WriteLine($"Unresolved: {item.Obligation.RuleId}  {item.Obligation.Claim}  ({reasonCode})");
        }

        WriteOpenP005(certificate);

        Console.WriteLine();
        Console.WriteLine("VERDICT:");
        Console.WriteLine($"  {certificate.Verdict}");
        Console.WriteLine();
        Console.WriteLine($"Certificate: {certificatePath}");
        if (!string.IsNullOrWhiteSpace(certificate.CertificateDigest))
        {
            Console.WriteLine($"Digest: {certificate.CertificateDigest}");
        }

        Console.WriteLine($"Hint: proof explain \"{certificatePath}\" for binding details.");
    }
}
