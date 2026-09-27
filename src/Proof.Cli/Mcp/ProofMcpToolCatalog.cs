namespace Proof.Cli;

/// <summary>
/// 정규 MCP 도구 이름. 서버 도구는 이 상수를
/// <c>[McpServerTool(Name = ...)]</c> 특성에 써서, 등록된 이름과
/// 능력 카탈로그가 어긋나지 않게 한다.
/// </summary>
internal static class ProofMcpToolNames
{
    internal const string Plan = "proof_plan";
    internal const string Obligations = "proof_obligations";
    internal const string Explain = "proof_explain";
    internal const string MapSuggest = "proof_map_suggest";
    internal const string Summary = "proof_summary";
    internal const string ConfigValidate = "proof_config_validate";
    internal const string CertificateVerify = "proof_certificate_verify";
    internal const string Verify = "proof_verify";
}

/// <summary>
/// 능력별로 나눈 MCP 도구 표면. 읽기 전용 도구는 항상
/// 등록된다. 실행 도구(Distill을 실행하거나 파일을 쓰는 것)는
/// 서버가 실행을 켠 채로 시작될 때만 등록된다.
/// </summary>
internal static class ProofMcpToolCatalog
{
    internal static readonly IReadOnlyList<string> ReadOnlyTools =
    [
        ProofMcpToolNames.Plan,
        ProofMcpToolNames.Obligations,
        ProofMcpToolNames.Explain,
        ProofMcpToolNames.MapSuggest,
        ProofMcpToolNames.Summary,
        ProofMcpToolNames.ConfigValidate,
        ProofMcpToolNames.CertificateVerify,
    ];

    internal static readonly IReadOnlyList<string> ExecutionTools =
    [
        ProofMcpToolNames.Verify,
    ];

    internal static IReadOnlyList<string> RegisteredTools(bool allowExec)
        => allowExec ? [.. ReadOnlyTools, .. ExecutionTools] : ReadOnlyTools;

    internal static bool IsExecutable(string toolName)
        => ExecutionTools.Contains(toolName, StringComparer.Ordinal);
}
