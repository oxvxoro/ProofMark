using System.Diagnostics;
using Distill.Core.Runs;

namespace Distill.Tests.TestSupport;

public sealed class GitWorkspace : IDisposable
{
    private GitWorkspace(string root)
    {
        Root = root;
    }

    public string Root { get; }

    public static GitWorkspace Create()
    {
        var root = Path.Combine(Path.GetTempPath(), $"distill-git-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, ".gitignore"), ".distill/\n");
        File.WriteAllText(Path.Combine(root, "source.txt"), "initial");
        RunGit(root, "init");
        RunGit(root, "config user.email test@example.com");
        RunGit(root, "config user.name Test");
        RunGit(root, "add .gitignore source.txt");
        RunGit(root, "commit -m init");
        return new GitWorkspace(root);
    }

    public void Dispose()
    {
        if (!Directory.Exists(Root))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void RunGit(string workingDirectory, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Failed to start git.");

        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"git {arguments} failed: {error}");
        }
    }
}
