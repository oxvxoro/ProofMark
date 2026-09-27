using System.Diagnostics;
using System.Text;

namespace Proof.Cli;

/// <summary>
/// Proof MCP 서버 뒤의 읽기 전용 도구 구현. 모든 메서드는
/// 같은 CLI 코드 경로를 재사용하고(로직을 복제하지 않음) CLI가 출력할
/// 것과 같은 JSON/마크다운 텍스트를 반환한다. 여기서는 파일을 쓰거나
/// HMAC 키를 읽지 않는다. 작성 명령(map add, review sign)은 의도적으로
/// 에이전트에 노출하지 않는다.
/// </summary>
internal static class ProofToolbox
{
    public static string ConfigValidate(string? root)
        => Run(root, () => ConfigValidateCommand.Validate());

    public static string CertificateVerify(string? certificatePath, bool requireSigned, string? root)
        => Run(root, () =>
        {
            var workspace = string.IsNullOrWhiteSpace(root)
                ? Directory.GetCurrentDirectory()
                : Path.GetFullPath(root);
            var path = string.IsNullOrWhiteSpace(certificatePath)
                ? CertificateExplainer.LatestCertificate(workspace)
                : certificatePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                Console.Error.WriteLine("No certificate found.");
                return 2;
            }

            return CertificateVerifyCommand.Verify(path, requireSigned);
        });

    public static string Plan(string? baseRevision, string? root, CancellationToken cancellationToken)
    {
        var args = new List<string> { "plan" };
        if (!string.IsNullOrWhiteSpace(baseRevision))
        {
            args.Add("--base");
            args.Add(baseRevision);
        }

        return RunProofCli(root, args);
    }

    public static string Obligations(bool openOnly, string? certificatePath, string? root)
        => Run(root, () => ObligationsCommand.Execute(openOnly, certificatePath));

    public static string Explain(string? certificatePath, string? obligationFilter, string? root)
        => Run(root, () => ExplainCommand.Execute(certificatePath, obligationFilter));

    public static string MapSuggest(string? root, CancellationToken cancellationToken)
        => RunProofCli(root, ["map", "suggest"]);

    public static string Summary(string? certificatePath, string format, string? root)
        => Run(root, () => SummaryCommand.Execute(certificatePath, format));

    public static string Verify(string? baseRevision, string output, string? profile, string? root, CancellationToken cancellationToken)
    {
        var args = new List<string> { "verify", "--output", output };
        if (!string.IsNullOrWhiteSpace(profile))
        {
            args.Add("--profile");
            args.Add(profile);
        }

        if (!string.IsNullOrWhiteSpace(baseRevision))
        {
            args.Add("--base");
            args.Add(baseRevision);
        }

        return RunProofCli(root, args, timeout: TimeSpan.FromMinutes(30));
    }

    /// <summary>
    /// 워크스페이스에서 <c>dotnet exec proof.dll</c>을 띄워 git/CodeMap이 MCP
    /// 호스트 프로세스 밖에서 돌게 한다(프로세스 안 git은 Windows의 MCP 호스팅에서 멈출 수 있다).
    /// </summary>
    private static string RunProofCli(string? root, IReadOnlyList<string> arguments, TimeSpan? timeout = null)
    {
        var workspace = string.IsNullOrWhiteSpace(root)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(root);
        var proofDll = Path.Combine(AppContext.BaseDirectory, "proof.dll");
        if (!File.Exists(proofDll))
        {
            return $"proof CLI not found beside MCP host: {proofDll}{Environment.NewLine}exit: 2";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workspace,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add(proofDll);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start proof CLI.");
        process.StandardInput.Close();
        var stdoutTask = Task.Run(() => process.StandardOutput.ReadToEnd());
        var stderrTask = Task.Run(() => process.StandardError.ReadToEnd());
        var waitMs = (int)(timeout ?? TimeSpan.FromMinutes(10)).TotalMilliseconds;
        if (!process.WaitForExit(waitMs))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // 최선의 노력.
            }

            return $"proof CLI timed out after {waitMs / 1000}s{Environment.NewLine}exit: 2";
        }

        Task.WaitAll([stdoutTask, stderrTask], TimeSpan.FromSeconds(30));
        var stdout = stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : string.Empty;
        var stderr = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : string.Empty;

        var output = stdout.TrimEnd();
        if (process.ExitCode == 0)
        {
            return output;
        }

        var message = new StringBuilder();
        if (output.Length > 0)
        {
            message.AppendLine(output);
        }

        if (stderr.Trim().Length > 0)
        {
            message.AppendLine(stderr.TrimEnd());
        }

        message.Append("exit: ").Append(process.ExitCode);
        return message.ToString().TrimEnd();
    }

    /// <summary>
    /// 짧은 CLI 명령을 프로세스 안에서 실행한다. stdout을 잡고 작업 디렉터리는
    /// 도구의 루트에 고정한다.
    /// </summary>
    private static string Run(string? root, Func<int> action)
    {
        var originalDirectory = Directory.GetCurrentDirectory();
        var originalOut = Console.Out;
        try
        {
            if (!string.IsNullOrWhiteSpace(root))
            {
                Directory.SetCurrentDirectory(Path.GetFullPath(root));
            }

            var outWriter = new StringWriter();
            Console.SetOut(outWriter);
            var exit = action();
            Console.Out.Flush();
            var output = outWriter.ToString().TrimEnd();
            if (exit == 0)
            {
                return output;
            }

            return $"{output}{(output.Length > 0 ? Environment.NewLine : string.Empty)}exit: {exit}";
        }
        finally
        {
            Console.SetOut(originalOut);
            Directory.SetCurrentDirectory(originalDirectory);
        }
    }
}
