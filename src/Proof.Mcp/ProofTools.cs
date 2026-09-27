using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Proof.Cli;

namespace Proof.Mcp;

internal static class ProofMcpServer
{
    public static async Task RunAsync(ProofMcpOptions options, CancellationToken cancellationToken)
    {
        // 빈 args. 우리 플래그(--allow-exec/--root)는 이 지점 전에
        // 파싱되며, 호스트 구성 제공자에게 넘겨서는 안 된다.
        var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
        builder.Logging.AddConsole(consoleLogOptions =>
            consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace);

        var policy = new McpWorkspacePolicy(options.Root, Directory.GetCurrentDirectory());
        var mcp = builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools(new ReadOnlyProofTools(policy));

        // 실행 도구는 선택이다. --allow-exec가 없으면 서버는 이전과
        // 같은 읽기 전용 표면을 말한다. proof_verify는 빠진다.
        if (options.AllowExec)
        {
            mcp.WithTools(new ExecutableProofTools(policy));
        }

        await builder.Build().RunAsync(cancellationToken);
    }
}

/// <summary>
/// 읽기 전용 Proof 도구. 모든 메서드는 같은 CLI 코드 경로를 재사용하고
/// 파일을 쓰거나 HMAC 키를 결코 건드리지 않는다.
/// </summary>
[McpServerToolType]
public sealed class ReadOnlyProofTools
{
    private readonly McpWorkspacePolicy _policy;

    internal ReadOnlyProofTools(McpWorkspacePolicy policy) => _policy = policy;

    [McpServerTool(Name = ProofMcpToolNames.Plan, ReadOnly = true), Description("Show the change set, impact, and proof obligations for the current workspace without running Distill checks.")]
    public string ProofPlan(
        string? baseRevision = null,
        string? root = null,
        CancellationToken cancellationToken = default)
        => RunGuarded(root, resolved => ProofToolbox.Plan(baseRevision, resolved, cancellationToken));

    [McpServerTool(Name = ProofMcpToolNames.Obligations, ReadOnly = true), Description("List certificate obligations (read-only query over the latest or given certificate).")]
    public string ProofObligations(
        bool openOnly = false,
        string? certificatePath = null,
        string? root = null)
        => RunGuardedPath(root, certificatePath, (resolved, path) => ProofToolbox.Obligations(openOnly, path, resolved));

    [McpServerTool(Name = ProofMcpToolNames.Explain, ReadOnly = true), Description("Explain why obligations are Unresolved, including evidence links, rejected bindings, and the suggested next authoring step.")]
    public string ProofExplain(
        string? certificatePath = null,
        string? obligationFilter = null,
        string? root = null)
        => RunGuardedPath(root, certificatePath, (resolved, path) => ProofToolbox.Explain(path, obligationFilter, resolved));

    [McpServerTool(Name = ProofMcpToolNames.MapSuggest, ReadOnly = true), Description("List open P005 symbols with heuristic test-map suggestions. Output is authored advice for a human to review; it is never evidence. Use `proof map add --accept` in a terminal to write an entry.")]
    public string ProofMapSuggest(
        string? root = null,
        CancellationToken cancellationToken = default)
        => RunGuarded(root, resolved => ProofToolbox.MapSuggest(resolved, cancellationToken));

    [McpServerTool(Name = ProofMcpToolNames.Summary, ReadOnly = true), Description("Render the latest certificate summary (verdict, reason code, proven/unresolved counts) as markdown or json. Read-only.")]
    public string ProofSummary(
        string format = "markdown",
        string? certificatePath = null,
        string? root = null)
        => RunGuardedPath(root, certificatePath, (resolved, path) => ProofToolbox.Summary(path, format, resolved));

    [McpServerTool(Name = ProofMcpToolNames.ConfigValidate, ReadOnly = true), Description("Validate proof.yml and distill.yml paths (read-only).")]
    public string ProofConfigValidate(string? root = null)
        => RunGuarded(root, resolved => ProofToolbox.ConfigValidate(resolved));

    [McpServerTool(Name = ProofMcpToolNames.CertificateVerify, ReadOnly = true), Description("Recompute and verify a change certificate digest and optional attestation (read-only).")]
    public string ProofCertificateVerify(
        string? certificatePath = null,
        bool requireSigned = false,
        string? root = null)
        => RunGuardedPath(root, certificatePath, (resolved, path) => ProofToolbox.CertificateVerify(path, requireSigned, resolved));

    private string RunGuarded(string? root, Func<string?, string> action)
        => _policy.TryResolve(root, out var resolved, out var error) ? action(resolved) : error!;

    private string RunGuardedPath(string? root, string? path, Func<string?, string?, string> action)
    {
        if (!_policy.TryResolve(root, out var resolved, out var error))
        {
            return error!;
        }

        if (!_policy.TryResolveContained(path, resolved, out var resolvedPath, out var pathError))
        {
            return pathError!;
        }

        return action(resolved, resolvedPath);
    }
}

/// <summary>
/// Distill을 실행하고 인증서를 쓰는 실행 도구. 서버가
/// <c>--allow-exec</c>로 시작될 때만 등록된다.
/// </summary>
[McpServerToolType]
public sealed class ExecutableProofTools
{
    private const string VerifyWarning =
        "Runs Distill; may take minutes; never invents evidence.";

    private readonly McpWorkspacePolicy _policy;

    internal ExecutableProofTools(McpWorkspacePolicy policy) => _policy = policy;

    [McpServerTool(Name = ProofMcpToolNames.Verify, ReadOnly = false), Description(VerifyWarning + " Verifies the change and writes a certificate under .proof/certificates.")]
    public string ProofVerify(
        string output = "compact",
        string? profile = null,
        string? baseRevision = null,
        string? root = null,
        CancellationToken cancellationToken = default)
        => _policy.TryResolve(root, out var resolved, out var error)
            ? ProofToolbox.Verify(baseRevision, output, profile, resolved, cancellationToken)
            : error!;
}

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        ProofMcpOptions options;
        try
        {
            options = ProofMcpOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };
        try
        {
            await ProofMcpServer.RunAsync(options, cts.Token);
            return 0;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return 130;
        }
    }
}
