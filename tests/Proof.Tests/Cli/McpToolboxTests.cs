using Proof.Cli;

namespace Proof.Tests;

public sealed class McpToolboxTests
{
    [Fact]
    public void Plan_through_toolbox_returns_plan_json()
    {
        var root = FindRepoRoot();
        var output = ProofToolbox.Plan(null, root, CancellationToken.None);
        Assert.DoesNotContain("exit: 2", output, StringComparison.Ordinal);
        Assert.StartsWith("{", output.TrimStart(), StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "proof.yml")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate Proofmark repository root (proof.yml).");
    }
}
