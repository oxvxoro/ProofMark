using System.Text.Json;
using System.Text.Json.Serialization;
using Proof.Core;

namespace Proof.Adapters.Distill;

internal sealed class DistillVerificationCache
{
    private static readonly JsonSerializerOptions CacheOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper) }
    };

    private readonly bool _cacheEnabled;

    internal DistillVerificationCache(bool cacheEnabled)
    {
        _cacheEnabled = cacheEnabled;
    }

    // 잘못된 캐시 항목이 판정을 만들어서는 안 된다. 캐싱은 선택이며,
    // 소스 다이제스트 + 재작성된 명령 + 프로필이 키고, 읽을 때 출처를
    // 다시 검증한다(다이제스트는 워크스페이스 dirty/clean 상태를 포함한다).
    internal bool IsCacheable(ProofPlan proofPlan, VerificationPlan verificationPlan)
    {
        var environmentEnabled = Environment.GetEnvironmentVariable("PROOF_CACHE");
        var environmentFlag = environmentEnabled is "1" or "true";
        return (environmentFlag || _cacheEnabled)
            && !string.IsNullOrWhiteSpace(proofPlan.SourceDigest);
    }

    internal static string ComputeCacheKey(ProofPlan proofPlan, VerificationPlan verificationPlan)
    {
        var commands = string.Join('\n', verificationPlan.Checks.Select(item =>
            $"{item.CheckId}|{item.Command}|{item.OverrideTarget ?? string.Empty}|{item.TestFilter ?? string.Empty}"));
        var payload = $"{proofPlan.SourceDigest}\n{verificationPlan.Profile}\n{commands}";
        return Sha256Hex.HashText(payload);
    }

    internal static IReadOnlyList<ProofEvidence>? TryReadCache(string workspaceRoot, string cacheKey, string? sourceDigest)
    {
        var path = CachePath(workspaceRoot, cacheKey);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var evidence = JsonSerializer.Deserialize<List<ProofEvidence>>(
                File.ReadAllText(path), CacheOptions);
            if (evidence is null)
            {
                return null;
            }

            // 출처 재검증. 계획된 소스 다이제스트를 갖지 않은 캐시 증거는
            // 통째로 버린다.
            return evidence.All(item => string.Equals(item.Provenance.SourceDigest, sourceDigest, StringComparison.Ordinal))
                ? evidence
                : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return null;
        }
    }

    internal static void WriteCache(string workspaceRoot, string cacheKey, IReadOnlyList<ProofEvidence> evidence)
    {
        try
        {
            var path = CachePath(workspaceRoot, cacheKey);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(evidence, CacheOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 캐시 쓰기 실패가 검증을 깨뜨려서는 안 된다.
        }
    }

    private static string CachePath(string workspaceRoot, string cacheKey)
        => Path.Combine(workspaceRoot, ".proof", "cache", $"{cacheKey}.json");
}
