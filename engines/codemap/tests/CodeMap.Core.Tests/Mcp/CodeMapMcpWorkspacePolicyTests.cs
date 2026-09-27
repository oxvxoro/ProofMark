using CodeMap.Mcp;

namespace CodeMap.Core.Tests;

public sealed class CodeMapMcpWorkspacePolicyTests
{
    [Fact]
    public void Policy_rejects_roots_outside_the_pin()
    {
        var parent = Path.Combine(Path.GetTempPath(), "codemap-mcp-scope-" + Guid.NewGuid());
        var pin = Path.Combine(parent, "repo");
        var policy = new McpWorkspacePolicy(pin);

        Assert.False(policy.TryResolve(parent, out _, out var parentError));
        Assert.StartsWith("Rejected root", parentError, StringComparison.Ordinal);
        Assert.Contains("outside the pinned workspace root", parentError, StringComparison.Ordinal);
    }
}
