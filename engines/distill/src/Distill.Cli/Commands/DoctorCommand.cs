using Distill.Core.Config;
using Distill.Testing.VSTest;
using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;

namespace Distill.Cli.Commands;

public static class DoctorCommand
{
    public static Command Create()
    {
        var command = new Command("doctor", "Check local environment for Distill prerequisites.");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            _ = parseResult;
            return await ExecuteAsync(cancellationToken).ConfigureAwait(false);
        });
        return command;
    }

    internal static async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        var workspaceRoot = Directory.GetCurrentDirectory();
        var issues = 0;

        issues += await CheckCommandAsync("dotnet", ["--version"], "dotnet SDK", cancellationToken).ConfigureAwait(false);
        issues += await CheckCommandAsync("git", ["--version"], "git", cancellationToken).ConfigureAwait(false);

        if (Directory.Exists(Path.Combine(workspaceRoot, ".git")))
        {
            Console.WriteLine("[ok] git repository root detected");
        }
        else
        {
            Console.WriteLine("[warn] current directory is not a git repository root");
            issues++;
        }

        try
        {
            var loggerDirectory = TestLoggerPathResolver.ResolveExtensionDirectory();
            Console.WriteLine($"[ok] Distill.TestLogger.dll found at {loggerDirectory}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[fail] Distill.TestLogger.dll: {ex.Message}");
            issues++;
        }

        var configPath = WorkspaceDiscovery.ResolveConfigPath(workspaceRoot);
        if (File.Exists(configPath))
        {
            try
            {
                DistillConfigLoader.Load(configPath);
                Console.WriteLine($"[ok] distill.yml valid at {configPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[fail] distill.yml invalid: {ex.Message}");
                issues++;
            }
        }
        else
        {
            Console.WriteLine("[warn] distill.yml not found (run `distill init`)");
            issues++;
        }

        var schemaPath = Path.Combine(workspaceRoot, "schemas", "distill-config-v1.json");
        if (File.Exists(schemaPath))
        {
            try
            {
                using var schema = JsonDocument.Parse(await File.ReadAllTextAsync(
                    schemaPath,
                    cancellationToken).ConfigureAwait(false));
                Console.WriteLine("[ok] distill-config-v1.json schema is readable");
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"[fail] distill-config-v1.json invalid: {ex.Message}");
                issues++;
            }
        }
        else
        {
            Console.WriteLine("[warn] distill-config-v1.json schema not found");
            issues++;
        }

        var latest = await RunContextResolver.ResolveLatestAsync(workspaceRoot, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (latest is null)
        {
            Console.WriteLine("[info] no verification runs found yet");
        }
        else
        {
            Console.WriteLine($"[ok] latest run accessible at {latest.Value.RunDirectory}");
        }

        return issues == 0 ? 0 : 1;
    }

    private static async Task<int> CheckCommandAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string label,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = string.Join(' ', arguments),
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            // MCP stdio 호스트에서는 상속된 stdin이 JSON-RPC 파이프다.
            // 자식이 그 파이프를 잡거나 stderr를 비우지 않으면 Windows에서 블록될 수 있다.
            process.Start();
            process.StandardInput.Close();
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var output = await stdoutTask.ConfigureAwait(false);
            await stderrTask.ConfigureAwait(false);

            if (process.ExitCode == 0)
            {
                Console.WriteLine($"[ok] {label}: {output.Trim()}");
                return 0;
            }

            Console.WriteLine($"[fail] {label} exited with code {process.ExitCode}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[fail] {label}: {ex.Message}");
            return 1;
        }
    }
}
