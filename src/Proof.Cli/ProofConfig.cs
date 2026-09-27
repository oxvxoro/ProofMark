using Proof.Core;

namespace Proof.Cli;

public sealed class ProofConfig
{
    public int Version { get; set; } = 2;

    public ProofSection Proof { get; set; } = new();

    public VerificationSection Verification { get; set; } = new();

    public PolicySection Policy { get; set; } = new();

    public AnalysisSection Analysis { get; set; } = new();

    public static ProofConfig Load(string workspaceRoot)
        => ProofConfigLoader.Load(workspaceRoot);

    internal static ProofConfig LoadForBootstrap(string workspaceRoot)
        => ProofConfigLoader.LoadForBootstrap(workspaceRoot);

    public static ProofConfig LoadedFromFile(string path, string? workspaceRoot = null)
        => ProofConfigLoader.LoadedFromFile(path, workspaceRoot);

    public void Validate(string? workspaceRoot = null, bool requireExistingAnalysisSolution = true)
        => ProofConfigSchemaValidator.Validate(this, workspaceRoot, requireExistingAnalysisSolution);

    public ImpactAnalysisSettings ToImpactSettings()
        => ProofConfigCompiler.ToImpactSettings(this);

    public static string ResolveConfigPath(string workspaceRoot)
        => ProofConfigLoader.ResolveConfigPath(workspaceRoot);

    public global::Proof.Core.ProofPolicy ToPolicy()
        => ProofConfigCompiler.ToPolicy(this);

    public static void RejectUnknownProperties(string yaml, YamlDotNet.Serialization.IDeserializer deserializer)
        => ProofConfigSchemaValidator.RejectUnknownProperties(yaml, deserializer);
}

public sealed record CompiledVerificationSettings(
    string DistillProfile,
    string DistillConfig,
    bool Cache,
    string? ImportManifest = null);

public sealed record CompiledBaseRevisionSettings(string Strategy, string? Ref);

public sealed record CompiledProofConfig(
    ProofPolicy Policy,
    ImpactAnalysisSettings Impact,
    CompiledVerificationSettings Verification,
    CompiledBaseRevisionSettings Base);

public sealed class ProofSection
{
    public BaseSection Base { get; set; } = new();
}

public sealed class BaseSection
{
    public string Strategy { get; set; } = "mergeBase";

    public string? Ref { get; set; }
}

public sealed class VerificationSection
{
    public string DistillProfile { get; set; } = "quick";

    public string DistillConfig { get; set; } = "distill.yml";

    public bool Cache { get; set; }

    // 선택적 CI 생성 검사 매니페스트. 설정되면, 계획된 검사가 모두 포함되고
    // 출처가 맞을 때만 검사 실행을 재사용한다. 아니면 모든
    // 검사를 다시 실행한다. 경로는 워크스페이스 루트 기준으로 해석한다.
    public string? ImportManifest { get; set; }
}

public sealed class PolicySection
{
    public string PublicApiCompatibility { get; set; } = "required";

    public string InternalConsumerCompatibility { get; set; } = "required";

    public string TestMapping { get; set; } = "required";

    public List<string>? TestMappingProjects { get; set; }

    public string StaticAnalysis { get; set; } = "off";

    public string AppContract { get; set; } = "off";

    public string Architecture { get; set; } = "off";

    public List<TestMapEntrySection> TestMaps { get; set; } = [];

    public List<PathRuleSection> Paths { get; set; } = [];

    public CiSection Ci { get; set; } = new();

    public UncertaintySection Uncertainty { get; set; } = new();

    public ManualReviewSection ManualReview { get; set; } = new();
}

public sealed class ManualReviewSection
{
    // 증거를 낼 수 있는 리뷰 파일 스키마 버전. null/빈 값은
    // 마이그레이션 기본값(v1과 v2 둘 다)이다. [2]로 두면 reviewer-bound
    // 리뷰를 요구하고 레거시 파일을 거부한다.
    public List<int>? AcceptedSchemas { get; set; }
}

public sealed class TestMapEntrySection
{
    public string Symbol { get; set; } = string.Empty;

    public List<string> Tests { get; set; } = [];
}

public sealed class PathRuleSection
{
    public string Match { get; set; } = string.Empty;

    public string Effect { get; set; } = "ignore";
}

public sealed class CiSection
{
    public List<string> FailOnUncertainCodes { get; set; } = [];
}

public sealed class UncertaintySection
{
    public string HeuristicImpact { get; set; } = "advisory";

    public string LocationUnknown { get; set; } = "blocking";

    public string ImpactTruncated { get; set; } = "blocking";

    public string CallerTruncated { get; set; } = "blocking";
}

public sealed class AnalysisSection
{
    public string? Solution { get; set; }

    public bool IndexBaseRevision { get; set; }

    public ImpactSection Impact { get; set; } = new();
}

public sealed class ImpactSection
{
    public string Profile { get; set; } = "code";

    public int BaseDepth { get; set; } = 2;

    public int PublicDepth { get; set; } = 3;

    public int MaxResults { get; set; } = 500;

    public int CallerPageSize { get; set; } = 50;

    public int CallerMaxResults { get; set; } = 50;

    public double MinConfidence { get; set; } = 0.75;
}
