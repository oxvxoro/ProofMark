using System.Globalization;
using System.Text.Json;
using Distill.Core.Config;
using Distill.Core.Evidence;
using Distill.Core.Planning;
using Distill.Core.Runs;
using Distill.Testing.VSTest;
using Proof.Core;

namespace Proof.Adapters.Distill;

internal sealed record ImportedCheckEntry(
    string? Artifact,
    string? Sha256,
    string? CommandDigest,
    string? Kind,
    string? Status,
    int? ExitCode);

internal sealed record ImportedCheckManifest(
    string? SourceDigest,
    Dictionary<string, ImportedCheckEntry>? Checks);

internal static class ImportedCheckManifestLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    internal static ImportedCheckManifest? TryLoad(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<ImportedCheckManifest>(File.ReadAllText(path), Options);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// CI가 만든 검사 매니페스트는 계획된 검사가 모두 포함되고 출처가
/// 맞을 때만 재사용한다. 소스 다이제스트, 검사 종류, 명령 정의
/// 다이제스트, 산출물 SHA-256이다. 하나라도 어긋나거나 일부 매니페스트면
/// 해석기가 거절하므로, 호출자는 오래되었거나 외부의 산출물을 믿지 않고
/// 검사를 다시 실행한다. 가져온 결과와 실행 결과를 부분적으로 섞는 일은
/// 절대 없다.
/// </summary>
internal static class ImportedCheckResolver
{
    internal static bool TryResolve(
        ProofPlan proofPlan,
        IReadOnlyList<PlannedCheck> plannedChecks,
        string workspaceRoot,
        ImportedCheckManifest manifest,
        out IReadOnlyList<CheckRunResult> results)
    {
        results = [];

        if (plannedChecks.Count == 0
            || string.IsNullOrWhiteSpace(proofPlan.SourceDigest)
            || manifest.Checks is not { Count: > 0 }
            || !string.Equals(manifest.SourceDigest, proofPlan.SourceDigest, StringComparison.Ordinal))
        {
            return false;
        }

        var resolved = new List<CheckRunResult>(plannedChecks.Count);
        foreach (var planned in plannedChecks)
        {
            if (!manifest.Checks.TryGetValue(planned.Id, out var entry)
                || !string.Equals(entry.Kind, planned.Definition.Kind, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(entry.CommandDigest, ComputeCommandDigest(planned.Definition), StringComparison.Ordinal)
                || !Enum.TryParse<VerificationStatus>(entry.Status, ignoreCase: true, out var status))
            {
                return false;
            }

            var artifact = ResolveArtifact(workspaceRoot, entry.Artifact, entry.Sha256);
            if (artifact is null)
            {
                return false;
            }

            var executedCases = TryParseTestCases(planned.Definition.Kind, artifact);
            resolved.Add(new CheckRunResult(
                planned.Id,
                planned.Definition.Kind,
                status,
                entry.ExitCode,
                [],
                SourceId: "imported",
                ArtifactPointer: artifact,
                Duration: TimeSpan.Zero,
                ExecutedCases: executedCases));
        }

        results = resolved;
        return true;
    }

    /// <summary>
    /// 검사 정의의 정규 다이제스트. CI 가져오기는 kind, command, source,
    /// timeout, stop-on-failure, dependsOn에 대해 같은 값을 계산해야 한다.
    /// </summary>
    internal static string ComputeCommandDigest(CheckConfig definition)
        => Sha256Hex.HashText(string.Join('\n', new[]
        {
            definition.Kind,
            definition.Command,
            definition.Source,
            definition.Timeout.ToString(CultureInfo.InvariantCulture),
            definition.StopOnFailure ? "1" : "0",
            string.Join(',', definition.DependsOn)
        }));

    private static string? ResolveArtifact(string workspaceRoot, string? artifact, string? expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(artifact) || string.IsNullOrWhiteSpace(expectedSha256))
        {
            return null;
        }

        var path = Path.GetFullPath(Path.Combine(workspaceRoot, artifact));
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var actual = Sha256Hex.HashStream(stream);
            return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase) ? path : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IReadOnlyList<TestCaseEvidence>? TryParseTestCases(string kind, string artifactPath)
    {
        if (!string.Equals(kind, "test", StringComparison.OrdinalIgnoreCase)
            || !artifactPath.EndsWith(".trx", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return new TrxParser().Parse(artifactPath, "imported-trx").Cases;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return null;
        }
    }
}
