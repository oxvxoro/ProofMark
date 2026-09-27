using Distill.Mcp;

namespace Distill.Tests;

public sealed class DistillMcpWorkspacePolicyTests
{
    [Fact]
    public void Policy_rejects_roots_outside_the_pin()
    {
        var parent = Path.Combine(Path.GetTempPath(), "distill-mcp-scope-" + Guid.NewGuid());
        var pin = Path.Combine(parent, "repo");
        var policy = new McpWorkspacePolicy(pin);

        Assert.False(policy.TryResolve(parent, out _, out var parentError));
        Assert.StartsWith("Rejected root", parentError, StringComparison.Ordinal);
        Assert.Contains("outside the pinned workspace root", parentError, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_reads_root_argument()
    {
        var options = DistillMcpOptions.Parse([DistillMcpOptions.RootFlag, "/repo"]);

        Assert.Equal("/repo", options.Root);
    }
}
