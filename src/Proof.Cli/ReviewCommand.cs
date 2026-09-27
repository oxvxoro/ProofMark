using System.CommandLine;
using System.Security.Cryptography;
using System.Text.Json;
using Proof.Core;
using Proof.Engine;

namespace Proof.Cli;

public static class ReviewCommand
{
    public static Command Create()
    {
        var subjectOption = new Option<string>("--subject")
        {
            Description = "P009 subject id (repo-relative path, '/'-separated).",
            Required = true
        };
        var digestOption = new Option<string?>("--digest")
        {
            Description = "Statement digest. Defaults to the current workspace snapshot's SourceDigest.",
            DefaultValueFactory = _ => null
        };
        var reviewerOption = new Option<string?>("--reviewer")
        {
            Description = "Reviewer identity, bound into the schema v2 signature payload.",
            DefaultValueFactory = _ => null
        };
        var outputOption = new Option<string?>("--output-dir")
        {
            Description = "Output directory. Defaults to .proof/reviews.",
            DefaultValueFactory = _ => null
        };
        var sign = new Command("sign", "Write a HMAC-signed manual review JSON that closes a P009 obligation.")
        {
            subjectOption,
            digestOption,
            reviewerOption,
            outputOption
        };
        sign.SetAction(async (parseResult, cancellationToken) => await ExecuteSignAsync(
            parseResult.GetValue(subjectOption)!,
            parseResult.GetValue(digestOption),
            parseResult.GetValue(reviewerOption),
            parseResult.GetValue(outputOption),
            cancellationToken).ConfigureAwait(false));

        var certificateOption = new Option<string?>("--certificate")
        {
            Description = "Certificate JSON path; P009 subjects come from its plan. Optional.",
            DefaultValueFactory = _ => null
        };
        var list = new Command("list", "List .proof/reviews/*.json with subject, digest, and signature validity.")
        {
            certificateOption
        };
        list.SetAction((parseResult, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ExecuteList(parseResult.GetValue(certificateOption)));
        });

        var review = new Command("review", "Manual-review authoring (P009).");
        review.Subcommands.Add(sign);
        review.Subcommands.Add(list);
        return review;
    }

    internal static async Task<int> ExecuteSignAsync(
        string subject,
        string? digest,
        string? reviewer,
        string? outputDir,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable)))
        {
            Console.Error.WriteLine(
                $"{AttestationHmac.KeyEnvironmentVariable} is not set; a review cannot be signed. Set the key and retry.");
            return 2;
        }

        var normalizedSubject = (subject ?? string.Empty).Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalizedSubject))
        {
            Console.Error.WriteLine("--subject is required (repo-relative path).");
            return 2;
        }

        try
        {
            var workspaceRoot = Directory.GetCurrentDirectory();
            var statementDigest = digest;
            string? warning = null;
            try
            {
                var workspace = await ProofWorkspace.PlanAsync(baseRevision: null, profile: null, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(statementDigest))
                {
                    statementDigest = workspace.Snapshot.SourceDigest;
                }

                // P009가 없는 주체에 리뷰를 쓰는 것은 증거
                // 위조가 아니다(파일은 계획에 실제로 있는 의무만 닫는다).
                // 그래서 경고하고 계속한다. 다이제스트가 어떻게
                // 주어졌는지는 상관없다.
                var hasP009 = workspace.Plan.Obligations.Any(item =>
                    item.RuleId == "P009"
                    && string.Equals(item.SubjectId, normalizedSubject, StringComparison.Ordinal));
                if (!hasP009)
                {
                    warning = $"No P009 obligation for subject '{normalizedSubject}' in the current plan; the review file is written but closes nothing.";
                }
            }
            catch (ProofConfigException exception)
            {
                // git worktree 밖이거나 base를 해석할 수 없으면 계획을
                // 만들 수 없다. 리뷰 파일은 그래도 작성된다. 서명자는
                // --digest를 명시적으로 넣어야 하고, 생산자는 여전히
                // 계획이 담을 다이제스트로 서명을 판단한다.
                if (string.IsNullOrWhiteSpace(statementDigest))
                {
                    Console.Error.WriteLine($"Warning: could not resolve the workspace snapshot digest ({exception.Message}); pass --digest explicitly.");
                    return 2;
                }
            }

            var key = Environment.GetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable)
                      ?? throw new ProofConfigException($"{AttestationHmac.KeyEnvironmentVariable} is empty.");
            // 새 리뷰는 reviewer-bound다(schema v2). 리뷰어는
            // 서명된 페이로드의 일부이지, 조언용 메타데이터가 아니다.
            var signature = AttestationHmac.ComputeHex(
                key,
                AttestationHmac.ReviewPayloadV2(statementDigest!, normalizedSubject, reviewer ?? string.Empty));
            var directory = Path.Combine(workspaceRoot, outputDir ?? Path.Combine(".proof", "reviews"));
            Directory.CreateDirectory(directory);
            var fileName = $"{Sanitize(normalizedSubject)}-{statementDigest![..Math.Min(8, statementDigest.Length)]}.json";
            var path = Path.Combine(directory, fileName);
            var payload = new ReviewFile(normalizedSubject, statementDigest, signature, reviewer, SchemaVersion: 2);
            File.WriteAllText(path, JsonSerializer.Serialize(payload, ProofJson.WireOptions));

            Console.WriteLine($"Review: {path}");
            Console.WriteLine($"Subject: {normalizedSubject}");
            Console.WriteLine($"Digest: {statementDigest}");
            if (warning is not null)
            {
                Console.Error.WriteLine($"Warning: {warning}");
            }

            return 0;
        }
        catch (ProofConfigException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    internal static int ExecuteList(string? certificatePath)
    {
        var workspaceRoot = Directory.GetCurrentDirectory();
        var directory = Path.Combine(workspaceRoot, ".proof", "reviews");
        if (!Directory.Exists(directory))
        {
            Console.WriteLine("REVIEWS (0)");
            return 0;
        }

        string? digest = null;
        if (!string.IsNullOrWhiteSpace(certificatePath))
        {
            var certificate = CertificateExplainer.LoadCertificate(workspaceRoot, certificatePath, out _);
            digest = certificate?.SourceDigest;
        }

        var files = Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        Console.WriteLine($"REVIEWS ({files.Length})");
        foreach (var file in files)
        {
            ReviewFile? payload = null;
            try
            {
                payload = JsonSerializer.Deserialize<ReviewFile>(File.ReadAllText(file), ProofJson.WireOptions);
            }
            catch (JsonException)
            {
            }

            if (payload is null)
            {
                Console.WriteLine($"  invalid  {Path.GetFileName(file)}");
                continue;
            }

            var schema = payload.SchemaVersion ?? ReviewSignature.LegacySchemaVersion;
            var valid = schema >= ReviewSignature.ReviewerBoundSchemaVersion
                ? ReviewSignature.IsValidV2(
                    payload.StatementDigest ?? string.Empty,
                    payload.SubjectId ?? string.Empty,
                    payload.Reviewer,
                    payload.Signature)
                : ManualReviewVerifier.IsSignatureValid(payload.StatementDigest ?? string.Empty, payload.SubjectId ?? string.Empty, payload.Signature);
            var stale = digest is not null
                && !string.IsNullOrWhiteSpace(payload.StatementDigest)
                && !string.Equals(payload.StatementDigest, digest, StringComparison.Ordinal);
            var state = !valid ? "invalid" : stale ? "stale" : "valid";
            Console.WriteLine($"  {state,-8} {payload.SubjectId}  {payload.StatementDigest}");
        }

        return 0;
    }

    private static string Sanitize(string subject)
        => string.Join('_', subject.Split('/', StringSplitOptions.RemoveEmptyEntries));

    internal sealed record ReviewFile(
        string? SubjectId,
        string? StatementDigest,
        string? Signature,
        string? Reviewer,
        int? SchemaVersion = null);
}

/// <summary>
/// <see cref="ReviewSignature"/>(Proof.Core)의 얇은 별칭. `review list`와
/// 증거 생산자가 상수 시간 비교 하나를 공유한다.
/// </summary>
internal static class ManualReviewVerifier
{
    public static bool IsSignatureValid(string statementDigest, string subjectId, string? signature)
        => ReviewSignature.IsValid(statementDigest, subjectId, signature);
}
