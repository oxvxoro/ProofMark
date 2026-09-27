using System.CommandLine;
using System.Text.Json;
using Proof.Core;
using Proof.Engine;

namespace Proof.Cli;

/// <summary>
/// 서명된 정책 예외의 수명 주기. 예외는 증명 판정 옆의 거버넌스 계층이며,
/// 판정을 결코 대체하지 않는다. 이 명령은 인증서, 의무 상태, 판정을
/// 결코 편집하지 않는다.
/// </summary>
public static class ExceptionCommand
{
    public static Command Create()
    {
        var command = new Command("exception", "Signed policy exceptions (governance only; never proof evidence).");
        command.Subcommands.Add(CreateList());
        command.Subcommands.Add(CreateVerify());
        command.Subcommands.Add(CreateSign());
        command.Subcommands.Add(CreateEvaluate());
        return command;
    }

    private static Command CreateList()
    {
        var rootOption = new Option<string?>("--root")
        {
            Description = "Workspace root. Defaults to the current directory.",
            DefaultValueFactory = _ => null
        };
        var formatOption = new Option<string>("--format")
        {
            Description = "Output format: markdown or json.",
            DefaultValueFactory = _ => "markdown"
        };
        var list = new Command("list", "List .proof/exceptions/*.json with signature and expiry state.")
        {
            rootOption,
            formatOption
        };
        list.SetAction((parseResult, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ExecuteList(
                parseResult.GetValue(rootOption),
                parseResult.GetValue(formatOption)!));
        });
        return list;
    }

    private static Command CreateVerify()
    {
        var fileArgument = new Argument<string>("file") { Description = "Exception JSON path." };
        var verify = new Command("verify", "Verify a policy-exception signature.") { fileArgument };
        verify.SetAction((parseResult, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ExecuteVerify(parseResult.GetValue(fileArgument)!));
        });
        return verify;
    }

    private static Command CreateSign()
    {
        var ruleOption = new Option<string>("--rule") { Description = "Rule id (e.g. P005).", Required = true };
        var subjectOption = new Option<string>("--subject")
        {
            Description = "Subject id, or * for a rule-wide exception.",
            DefaultValueFactory = _ => PolicyExceptionSignature.AllSubjects
        };
        var ownerOption = new Option<string>("--owner") { Description = "Owning team or person.", Required = true };
        var reasonOption = new Option<string>("--reason") { Description = "Why the exception is acceptable.", Required = true };
        var ticketOption = new Option<string?>("--ticket") { DefaultValueFactory = _ => null };
        var sourceDigestOption = new Option<string?>("--source-digest")
        {
            Description = "Bind the exception to one source digest. Omit for a rule/subject-scoped exception.",
            DefaultValueFactory = _ => null
        };
        var expiresOption = new Option<string?>("--expires")
        {
            Description = "Expiry as an ISO-8601 timestamp (e.g. 2026-10-31T00:00:00Z). Required for a time-bounded exception.",
            DefaultValueFactory = _ => null
        };
        var signerOption = new Option<string?>("--signer") { DefaultValueFactory = _ => null };
        var outputOption = new Option<string?>("--output-dir") { DefaultValueFactory = _ => null };
        var sign = new Command("sign", "Write a HMAC-signed policy exception (authoring; human-only).")
        {
            ruleOption,
            subjectOption,
            ownerOption,
            reasonOption,
            ticketOption,
            sourceDigestOption,
            expiresOption,
            signerOption,
            outputOption
        };
        sign.SetAction((parseResult, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ExecuteSign(
                parseResult.GetValue(ruleOption)!,
                parseResult.GetValue(subjectOption)!,
                parseResult.GetValue(ownerOption)!,
                parseResult.GetValue(reasonOption)!,
                parseResult.GetValue(ticketOption),
                parseResult.GetValue(sourceDigestOption),
                parseResult.GetValue(expiresOption),
                parseResult.GetValue(signerOption),
                parseResult.GetValue(outputOption)));
        });
        return sign;
    }

    private static Command CreateEvaluate()
    {
        var certificateOption = new Option<string?>("--certificate")
        {
            Description = "Certificate JSON path. Defaults to the latest .proof/certificates entry.",
            DefaultValueFactory = _ => null
        };
        var rootOption = new Option<string?>("--root")
        {
            Description = "Workspace root. Defaults to the current directory.",
            DefaultValueFactory = _ => null
        };
        var formatOption = new Option<string>("--format")
        {
            Description = "Output format: markdown or json.",
            DefaultValueFactory = _ => "markdown"
        };
        var gateOption = new Option<bool>("--gate")
        {
            Description = "Exit 0 only when exceptions clear an obligation-driven merge block (open required obligations covered, no blocking constraint). Use this in CI.",
            DefaultValueFactory = _ => false
        };
        var evaluate = new Command("evaluate", "Report whether valid exceptions cover the certificate's open required obligations (governance only).")
        {
            certificateOption,
            rootOption,
            formatOption,
            gateOption
        };
        evaluate.SetAction((parseResult, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ExecuteEvaluate(
                parseResult.GetValue(certificateOption),
                parseResult.GetValue(rootOption),
                parseResult.GetValue(formatOption)!,
                parseResult.GetValue(gateOption)));
        });
        return evaluate;
    }

    internal static int ExecuteList(string? root, string format)
    {
        var workspace = ResolveRoot(root);
        var loaded = PolicyExceptionStore.Load(workspace);
        var now = DateTimeOffset.UtcNow;
        var views = loaded
            .Select(item => new ExceptionView(
                item.Path,
                item.Value?.RuleId,
                item.Value?.SubjectId,
                item.Value?.Owner,
                item.Value?.ExpiresAt,
                item.Value is null ? "invalid" : PolicyExceptionGovernance.EvaluateState(item.Value, now).ToString().ToLowerInvariant()))
            .ToArray();

        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(JsonSerializer.Serialize(views, ProofJson.WireOptions));
            return 0;
        }

        Console.WriteLine($"EXCEPTIONS ({views.Length})");
        foreach (var view in views)
        {
            Console.WriteLine($"  {view.State,-16} {view.RuleId ?? "(invalid)"}  {view.SubjectId}  owner={view.Owner}  expires={view.ExpiresAt?.ToString("u") ?? "none"}");
        }

        return 0;
    }

    internal static int ExecuteVerify(string path)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Exception not found: {path}");
            return 2;
        }

        PolicyException? exception;
        try
        {
            exception = JsonSerializer.Deserialize<PolicyException>(File.ReadAllText(path), ProofJson.WireOptions);
        }
        catch (JsonException exceptionError)
        {
            Console.Error.WriteLine($"Invalid exception JSON: {exceptionError.Message}");
            return 2;
        }

        if (exception is null)
        {
            Console.Error.WriteLine("Exception deserialized to null.");
            return 2;
        }

        var state = PolicyExceptionGovernance.EvaluateState(exception, DateTimeOffset.UtcNow);
        Console.WriteLine($"Exception: {path}");
        Console.WriteLine($"State:     {state.ToString().ToLowerInvariant()}");
        Console.WriteLine($"Rule:      {exception.RuleId}");
        Console.WriteLine($"Subject:   {exception.SubjectId}");
        Console.WriteLine($"Owner:     {exception.Owner}");
        Console.WriteLine($"Expires:   {exception.ExpiresAt?.ToString("u") ?? "none"}");
        return state == PolicyExceptionState.Valid ? 0 : 1;
    }

    internal static int ExecuteSign(
        string rule,
        string subject,
        string owner,
        string reason,
        string? ticket,
        string? sourceDigest,
        string? expires,
        string? signer,
        string? outputDir)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AttestationHmac.KeyEnvironmentVariable)))
        {
            Console.Error.WriteLine(
                $"{AttestationHmac.KeyEnvironmentVariable} is not set; an exception cannot be signed. Set the key and retry.");
            return 2;
        }

        DateTimeOffset? expiresAt = null;
        if (!string.IsNullOrWhiteSpace(expires))
        {
            if (!DateTimeOffset.TryParse(
                    expires,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                Console.Error.WriteLine($"--expires is not a valid ISO-8601 timestamp: {expires}");
                return 2;
            }

            expiresAt = parsed;
        }

        var unsigned = new PolicyException(
            rule,
            string.IsNullOrWhiteSpace(subject) ? PolicyExceptionSignature.AllSubjects : subject,
            owner,
            reason,
            ticket,
            sourceDigest,
            expiresAt,
            signer,
            Signature: null);
        var signature = PolicyExceptionSignature.Sign(unsigned)
                        ?? throw new ProofConfigException($"{AttestationHmac.KeyEnvironmentVariable} is empty.");
        var signed = unsigned with { Signature = signature };

        var workspaceRoot = Directory.GetCurrentDirectory();
        var directory = Path.Combine(workspaceRoot, outputDir ?? Path.Combine(".proof", "exceptions"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{Sanitize(rule)}-{Sanitize(signed.SubjectId)}-{signature[..8]}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(signed, ProofJson.WireOptions));

        Console.WriteLine($"Exception: {path}");
        Console.WriteLine($"Rule:      {signed.RuleId}");
        Console.WriteLine($"Subject:   {signed.SubjectId}");
        Console.WriteLine($"Expires:   {signed.ExpiresAt?.ToString("u") ?? "none"}");
        if (IsIgnoredProofPath(path))
        {
            var relative = Path.GetRelativePath(workspaceRoot, path).Replace('\\', '/');
            Console.WriteLine($"Note: .proof/ is gitignored; commit with: git add -f {relative}");
        }

        return 0;
    }

    private static bool IsIgnoredProofPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/.proof/", StringComparison.OrdinalIgnoreCase);
    }

    internal static int ExecuteEvaluate(string? certificatePath, string? root, string format, bool gate = false)
    {
        var workspace = ResolveRoot(root);
        var resolvedCertificate = string.IsNullOrWhiteSpace(certificatePath)
            ? CertificateExplainer.LatestCertificate(workspace)
            : certificatePath;
        if (string.IsNullOrWhiteSpace(resolvedCertificate) || !File.Exists(resolvedCertificate))
        {
            Console.Error.WriteLine($"Certificate not found: {resolvedCertificate ?? "(none)"}");
            return 2;
        }

        var certificate = CertificateExplainer.LoadCertificate(workspace, resolvedCertificate, out var error);
        if (certificate is null)
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        var report = PolicyExceptionEvaluator.Evaluate(certificate, resolvedCertificate, PolicyExceptionStore.Load(workspace));
        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(JsonSerializer.Serialize(report, ProofJson.WireOptions));
        }
        else
        {
            Console.WriteLine(PolicyExceptionEvaluator.Render(report));
        }

        // 거버넌스 종료 코드만 다룬다. --gate가 있으면 0은 예외가 의무가
        // 이끄는 merge block을 해제했다는 뜻이다(열린 필수 의무가 덮이고,
        // blocking constraint가 없다). --gate가 없으면 0은 "미충족이 남지 않음"이다.
        // 인증서 판정은 조회하지도 바꾸지도 않는다.
        return gate
            ? (PolicyExceptionEvaluator.CanClearMergeBlock(report) ? 0 : 1)
            : (report.Uncovered == 0 ? 0 : 1);
    }

    private static string ResolveRoot(string? root)
        => string.IsNullOrWhiteSpace(root) ? Directory.GetCurrentDirectory() : Path.GetFullPath(root);

    private static string Sanitize(string value)
        => string.Join('_', value.Split(['/', '\\', '*', ':'], StringSplitOptions.RemoveEmptyEntries));

    internal sealed record ExceptionView(
        string Path,
        string? RuleId,
        string? SubjectId,
        string? Owner,
        DateTimeOffset? ExpiresAt,
        string State);
}
