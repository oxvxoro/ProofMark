namespace Proof.Cli;

/// <summary>
/// Proof MCP 서버의 시작 옵션. 실행 도구는 선택이다. 기본
/// 표면은 읽기 전용이고, <c>proof_verify</c>는 <c>--allow-exec</c>
/// (또는 <c>PROOF_MCP_ALLOW_EXEC</c>)가 있을 때만 등록된다.
/// </summary>
internal sealed record ProofMcpOptions(bool AllowExec, string? Root)
{
    internal const string AllowExecFlag = "--allow-exec";
    internal const string RootFlag = "--root";
    internal const string AllowExecEnvironmentVariable = "PROOF_MCP_ALLOW_EXEC";

    internal static ProofMcpOptions Parse(IReadOnlyList<string> args)
        => Parse(args, Environment.GetEnvironmentVariable(AllowExecEnvironmentVariable));

    internal static ProofMcpOptions Parse(IReadOnlyList<string> args, string? allowExecEnvironment)
    {
        ArgumentNullException.ThrowIfNull(args);
        var allowExec = IsTruthy(allowExecEnvironment);
        string? root = null;
        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case AllowExecFlag:
                    allowExec = true;
                    break;
                case RootFlag:
                    if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        throw new ArgumentException($"{RootFlag} requires a directory argument.");
                    }

                    root = args[++index];
                    break;
            }
        }

        return new ProofMcpOptions(allowExec, root);
    }

    internal static bool IsTruthy(string? value)
        => value is not null
           && (value.Equals("1", StringComparison.Ordinal)
               || value.Equals("true", StringComparison.OrdinalIgnoreCase));
}
