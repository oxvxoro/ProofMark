using System.CommandLine;

namespace CodeMap.Cli;

/// <summary>
/// CLI 프로세스 호스트. 명령 구성과 처리기는 partial 명령 파일에서
/// 테스트할 수 있고, 이 클래스는 프로세스 수명과 취소를 소유한다.
/// </summary>
internal static class CliHost
{
    internal static async Task<int> RunAsync(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        RootCommand rootCommand = Program.CreateRootCommand(cancellation.Token);
        var invocationCode = await rootCommand.InvokeAsync(args);
        return Environment.ExitCode != 0 ? Environment.ExitCode : invocationCode;
    }
}
