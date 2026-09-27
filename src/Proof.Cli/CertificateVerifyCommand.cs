using System.CommandLine;
using System.Text.Json;
using Proof.Core;
using Proof.Engine;

namespace Proof.Cli;

public static class CertificateVerifyCommand
{
    public static Command Create()
    {
        var certificate = new Command("certificate", "Certificate commands");
        var fileArgument = new Argument<string>("file")
        {
            Description = "Certificate JSON path."
        };
        var requireSignedOption = new Option<bool>("--require-signed")
        {
            Description = "Fail verification when the attestation is unsigned.",
            DefaultValueFactory = _ => false
        };
        var trustedIssuerOption = new Option<string[]>("--trusted-issuer")
        {
            Description = "Trusted signer identity (repeatable). When set, the attestation issuer must be listed.",
            AllowMultipleArgumentsPerToken = true
        };
        var repositoryOption = new Option<string?>("--repository")
        {
            Description = "Trusted repository claim (owner/repo). When set, the attestation repository must match.",
            DefaultValueFactory = _ => null
        };
        var allowedRefOption = new Option<string[]>("--allow-ref")
        {
            Description = "Allowed attestation ref (repeatable). When set, the ref (or legacy branch) claim must be listed.",
            AllowMultipleArgumentsPerToken = true
        };
        var allowedWorkflowOption = new Option<string[]>("--allow-workflow")
        {
            Description = "Allowed workflow ref (repeatable). When set, the workflowRef claim must be listed.",
            AllowMultipleArgumentsPerToken = true
        };
        var expectedCommitOption = new Option<string?>("--expected-commit-sha")
        {
            Description = "Expected commit SHA claim. When set, the attested commit must match.",
            DefaultValueFactory = _ => null
        };
        var toolchainBinaryOption = new Option<string[]>("--toolchain-binary")
        {
            Description = "Toolchain binary path (repeatable). When set, its SHA-256 must match a binary digest recorded in the certificate toolchain.",
            AllowMultipleArgumentsPerToken = true
        };
        var verify = new Command("verify", "Recompute and check the canonical certificate digest.")
        {
            fileArgument,
            requireSignedOption,
            trustedIssuerOption,
            repositoryOption,
            allowedRefOption,
            allowedWorkflowOption,
            expectedCommitOption,
            toolchainBinaryOption
        };
        verify.SetAction((parseResult, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Verify(
                parseResult.GetValue(fileArgument)!,
                parseResult.GetValue(requireSignedOption),
                parseResult.GetValue(trustedIssuerOption),
                parseResult.GetValue(repositoryOption),
                parseResult.GetValue(allowedRefOption),
                parseResult.GetValue(allowedWorkflowOption),
                parseResult.GetValue(expectedCommitOption),
                parseResult.GetValue(toolchainBinaryOption)));
        });
        certificate.Subcommands.Add(verify);

        var leftArgument = new Argument<string>("a") { Description = "First certificate JSON path." };
        var rightArgument = new Argument<string>("b") { Description = "Second certificate JSON path." };
        var diff = new Command("diff", "Compare two certificates without recomputing either statement.")
        {
            leftArgument,
            rightArgument
        };
        diff.SetAction((parseResult, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Diff(parseResult.GetValue(leftArgument)!, parseResult.GetValue(rightArgument)!));
        });
        certificate.Subcommands.Add(diff);
        return certificate;
    }

    internal static int Diff(string leftPath, string rightPath)
    {
        var left = CertificateExplainer.LoadCertificate(Directory.GetCurrentDirectory(), leftPath, out var leftError);
        if (left is null)
        {
            Console.Error.WriteLine(leftError);
            return 2;
        }

        var right = CertificateExplainer.LoadCertificate(Directory.GetCurrentDirectory(), rightPath, out var rightError);
        if (right is null)
        {
            Console.Error.WriteLine(rightError);
            return 2;
        }

        var diff = CertificateExplainer.Diff(left, right);
        Console.WriteLine($"Verdict:    {diff.LeftVerdict} -> {diff.RightVerdict}");
        Console.WriteLine($"Statement:  {diff.LeftStatementDigest} -> {diff.RightStatementDigest}");
        Console.WriteLine($"Certificate:{diff.LeftCertificateDigest} -> {diff.RightCertificateDigest}");
        WriteSet("Obligations only in A", diff.ObligationsOnlyInLeft);
        WriteSet("Obligations only in B", diff.ObligationsOnlyInRight);
        WriteSet("Evidence only in A", diff.EvidenceOnlyInLeft);
        WriteSet("Evidence only in B", diff.EvidenceOnlyInRight);
        return 0;
    }

    private static void WriteSet(string label, IReadOnlyList<string> values)
    {
        Console.WriteLine($"{label} ({values.Count}):");
        foreach (var value in values)
        {
            Console.WriteLine($"  {value}");
        }
    }

    internal static int Verify(
        string path,
        bool requireSigned = false,
        IReadOnlyList<string>? trustedIssuers = null,
        string? repository = null,
        IReadOnlyList<string>? allowedRefs = null,
        IReadOnlyList<string>? allowedWorkflows = null,
        string? expectedCommitSha = null,
        IReadOnlyList<string>? toolchainBinaries = null)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Certificate not found: {path}");
            return 2;
        }

        var json = File.ReadAllText(path);
        var certificate = JsonSerializer.Deserialize<ChangeCertificate>(json, ProofJson.WireOptions);
        if (certificate is null)
        {
            Console.Error.WriteLine("Certificate deserialized to null.");
            return 2;
        }

        var expected = certificate.CertificateDigest;
        var actual = CertificateCanonicalHasher.ComputeDigest(certificate);
        if (certificate.SchemaVersion >= 3)
        {
            var statement = CertificateCanonicalHasher.ComputeStatementDigest(certificate);
            if (!string.IsNullOrWhiteSpace(certificate.StatementDigest)
                && !string.Equals(certificate.StatementDigest, statement, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("Certificate statement digest mismatch.");
                Console.Error.WriteLine($"Expected: {certificate.StatementDigest}");
                Console.Error.WriteLine($"Actual:   {statement}");
                return 1;
            }
        }
        if (string.IsNullOrWhiteSpace(expected))
        {
            Console.WriteLine("Certificate has no digest (schema v1 or unsigned).");
            Console.WriteLine($"Computed: {actual}");
            return 0;
        }

        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Certificate digest mismatch.");
            Console.Error.WriteLine($"Expected: {expected}");
            Console.Error.WriteLine($"Actual:   {actual}");
            return 1;
        }

        Console.WriteLine("Certificate digest is valid.");
        Console.WriteLine(actual);
        var toolchainExit = VerifyToolchainBinaries(certificate.Toolchain, toolchainBinaries);
        if (toolchainExit != 0)
        {
            return toolchainExit;
        }

        if (certificate.ArtifactManifest is { Count: > 0 } manifest)
        {
            var manifestFailure = VerifyArtifactManifest(manifest);
            if (manifestFailure is not null)
            {
                Console.Error.WriteLine(manifestFailure);
                return 1;
            }

            Console.WriteLine($"Artifact manifest verified ({manifest.Count} entries).");
        }

        if (!VerifyAttestation(
                certificate,
                new TrustPolicy(requireSigned, trustedIssuers, repository, allowedRefs, allowedWorkflows, expectedCommitSha),
                out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        return 0;
    }

    // 플래그가 없으면 바이너리를 요구하지 않는다. 도구를 올린 뒤에도 옛 증명서가 통과해야 한다.
    // 파일이 없으면 2, 기록된 다이제스트와 다르면 1이다. 기록이 없으면 건너뛴다.
    internal static int VerifyToolchainBinaries(ToolchainIdentity? toolchain, IReadOnlyList<string>? binaries)
    {
        if (binaries is not { Count: > 0 })
        {
            return 0;
        }

        var missing = binaries.FirstOrDefault(binary => !File.Exists(binary));
        if (missing is not null)
        {
            Console.Error.WriteLine($"Toolchain binary not found: {missing}");
            return 2;
        }

        string?[] candidates = [toolchain?.ProofBinarySha256, toolchain?.CodeMapBinarySha256, toolchain?.DistillBinarySha256];
        var recorded = candidates.OfType<string>().Where(value => value.Length > 0).ToArray();
        if (recorded.Length == 0)
        {
            Console.WriteLine("Certificate records no toolchain binary digests; toolchain binary check skipped.");
            return 0;
        }

        foreach (var binary in binaries)
        {
            var digest = CertificateCanonicalHasher.HashFile(binary);
            if (!recorded.Contains(digest, StringComparer.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"Toolchain binary digest mismatch: {binary}");
                return 1;
            }

            Console.WriteLine($"Toolchain binary verified: {binary}");
        }

        return 0;
    }

    // 매니페스트 경로는 저장소 상대('/' 구분)로 정규화되므로,
    // 이 명령은 저장소 루트에서 실행되어야 한다.
    internal static string? VerifyArtifactManifest(IReadOnlyList<EvidenceArtifactManifestEntry> manifest)
    {
        foreach (var entry in manifest)
        {
            if (string.IsNullOrWhiteSpace(entry.RelativePath))
            {
                return $"Artifact manifest entry '{entry.LogicalId}' has no relative path.";
            }

            var path = entry.RelativePath.Replace('\\', '/');
            if (!File.Exists(path))
            {
                return $"Artifact file not found: {entry.RelativePath} (entry {entry.LogicalId}).";
            }

            var actual = CertificateCanonicalHasher.HashFile(path);
            if (!string.Equals(actual, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return $"Artifact hash mismatch for {entry.RelativePath} (entry {entry.LogicalId}).";
            }
        }

        return null;
    }

    private static bool VerifyAttestation(ChangeCertificate certificate, TrustPolicy policy, out string message)
    {
        if (certificate.Attestation is null)
        {
            message = "Certificate has no attestation (unsigned certificate).";
            Console.WriteLine(message);
            // 신뢰 발급자 허용 목록이나 어떤 신원 제약도 서명자 없이는
            // 만족될 수 없다. 증명이 없는 인증서는 --require-signed가
            // 설정되지 않았어도 신뢰되지 않는다.
            var hasConstraint = policy.TrustedIssuers is { Count: > 0 }
                || !string.IsNullOrWhiteSpace(policy.Repository)
                || policy.AllowedRefs is { Count: > 0 }
                || policy.AllowedWorkflows is { Count: > 0 }
                || !string.IsNullOrWhiteSpace(policy.ExpectedCommitSha);
            return !policy.RequireSigned && !hasConstraint;
        }

        if (!string.IsNullOrWhiteSpace(certificate.StatementDigest)
            && !string.Equals(
                certificate.StatementDigest,
                certificate.Attestation.StatementDigest,
                StringComparison.OrdinalIgnoreCase))
        {
            message = "Attestation statement digest does not match the certificate statement digest.";
            return false;
        }

        var verifier = AttestationProviderRegistry.Resolve(certificate.Attestation.Provider);
        var result = verifier
            .VerifyAsync(certificate.Attestation, policy, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        if (result.Trusted)
        {
            message = $"Attestation trusted (provider: {certificate.Attestation.Provider}).";
            return true;
        }

        message = $"Attestation not trusted: {result.Reason}";
        return false;
    }
}
