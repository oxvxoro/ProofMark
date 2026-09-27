using System.ComponentModel;
using Distill.Cli.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Distill.Mcp;

public static class DistillMcpServer
{
    public static async Task RunAsync(DistillMcpOptions options, CancellationToken cancellationToken)
    {
        var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
        builder.Logging.AddConsole(consoleLogOptions =>
            consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.AddSingleton(new DistillMcpHostContext(options));
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<DistillTools>();
        await builder.Build().RunAsync(cancellationToken);
    }
}

[McpServerToolType]
public sealed class DistillTools(DistillMcpHostContext hostContext)
{
    private readonly McpWorkspacePolicy _policy = hostContext.WorkspacePolicy;

    [McpServerTool, Description("Show the Failure Pack from the latest or specified Distill run.")]
    public Task<string> DistillReport(string? run = null, string? root = null, CancellationToken cancellationToken = default)
        => RunGuarded(root, resolved => DistillToolbox.RunAsync(resolved, () => ReportCommand.ExecuteAsync(run, cancellationToken)));

    [McpServerTool, Description("Filter normalized diagnostics from the latest Distill run.")]
    public Task<string> DistillDiagnostics(string? kind = null, string? run = null, string? root = null, CancellationToken cancellationToken = default)
        => RunGuarded(root, resolved => DistillToolbox.RunAsync(resolved, () => DiagnosticsCommand.ExecuteAsync(kind, run, cancellationToken)));

    [McpServerTool, Description("Show raw check artifacts from the latest Distill run.")]
    public Task<string> DistillRaw(string check, int tail = 200, string? grep = null, string? run = null, string? root = null, CancellationToken cancellationToken = default)
        => RunGuarded(root, resolved => DistillToolbox.RunAsync(resolved, () => RawCommand.ExecuteAsync(check, tail, grep, run, cancellationToken)));

    [McpServerTool, Description("Validate distill.yml and workspace prerequisites.")]
    public Task<string> DistillDoctor(string? root = null, CancellationToken cancellationToken = default)
        => RunGuarded(root, resolved => DistillToolbox.RunAsync(resolved, () => DoctorCommand.ExecuteAsync(cancellationToken)));

    private Task<string> RunGuarded(string? root, Func<string, Task<string>> action)
    {
        if (!_policy.TryResolve(root, out var resolved, out var error))
        {
            return Task.FromResult(error!);
        }

        return action(resolved);
    }
}

internal static class DistillToolbox
{
    internal static async Task<string> RunAsync(string workspaceRoot, Func<Task<int>> action)
    {
        var previousOut = Console.Out;
        var previousErr = Console.Error;
        var previousDirectory = Directory.GetCurrentDirectory();
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            Directory.SetCurrentDirectory(workspaceRoot);
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var exit = await action().ConfigureAwait(false);
            var output = stdout.ToString();
            var errors = stderr.ToString();
            if (!string.IsNullOrEmpty(errors))
            {
                output += errors;
            }

            return string.IsNullOrEmpty(output) ? $"exit: {exit}" : output.TrimEnd() + Environment.NewLine + $"exit: {exit}";
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousErr);
            Directory.SetCurrentDirectory(previousDirectory);
        }
    }
}

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        DistillMcpOptions options;
        try
        {
            options = DistillMcpOptions.Parse(args);
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
        await DistillMcpServer.RunAsync(options, cts.Token);
        return 0;
    }
}
