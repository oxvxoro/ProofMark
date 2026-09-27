using Proof.Cli;

namespace Proof.Tests;

public sealed class McpCapabilityTests
{
    [Fact]
    public void Parse_defaults_to_read_only_workstation()
    {
        var options = ProofMcpOptions.Parse([], allowExecEnvironment: null);

        Assert.False(options.AllowExec);
        Assert.Null(options.Root);
    }

    [Fact]
    public void Parse_enables_execution_with_flag()
    {
        var options = ProofMcpOptions.Parse([ProofMcpOptions.AllowExecFlag], allowExecEnvironment: null);

        Assert.True(options.AllowExec);
    }

    [Fact]
    public void Parse_enables_execution_with_environment()
    {
        Assert.True(ProofMcpOptions.Parse([], "1").AllowExec);
        Assert.True(ProofMcpOptions.Parse([], "true").AllowExec);
        Assert.True(ProofMcpOptions.Parse([], "TRUE").AllowExec);
        Assert.False(ProofMcpOptions.Parse([], "0").AllowExec);
        Assert.False(ProofMcpOptions.Parse([], null).AllowExec);
    }

    [Fact]
    public void Parse_reads_root_argument()
    {
        var options = ProofMcpOptions.Parse([ProofMcpOptions.RootFlag, "/repo"], allowExecEnvironment: null);

        Assert.Equal("/repo", options.Root);
    }

    [Fact]
    public void Parse_rejects_root_without_value()
    {
        Assert.Throws<ArgumentException>(() =>
            ProofMcpOptions.Parse([ProofMcpOptions.RootFlag], allowExecEnvironment: null));
    }

    [Fact]
    public void Catalog_hides_execution_tools_unless_enabled()
    {
        var readOnly = ProofMcpToolCatalog.RegisteredTools(allowExec: false);

        Assert.DoesNotContain(ProofMcpToolNames.Verify, readOnly);
        Assert.Contains(ProofMcpToolNames.Plan, readOnly);
        Assert.Contains(ProofMcpToolNames.CertificateVerify, readOnly);

        var executable = ProofMcpToolCatalog.RegisteredTools(allowExec: true);
        Assert.Contains(ProofMcpToolNames.Verify, executable);
        Assert.True(ProofMcpToolCatalog.IsExecutable(ProofMcpToolNames.Verify));
        Assert.False(ProofMcpToolCatalog.IsExecutable(ProofMcpToolNames.Plan));
    }

    [Fact]
    public void Policy_resolves_empty_root_to_the_pin()
    {
        var pin = Path.Combine(Path.GetTempPath(), "proof-mcp-pin");
        var policy = new McpWorkspacePolicy(pin);

        Assert.True(policy.TryResolve(null, out var resolved, out var error));
        Assert.Null(error);
        Assert.Equal(Path.GetFullPath(pin), resolved);
    }

    [Fact]
    public void Policy_accepts_the_pin_and_its_descendants()
    {
        var pin = Path.Combine(Path.GetTempPath(), "proof-mcp-pin");
        var policy = new McpWorkspacePolicy(pin);

        Assert.True(policy.TryResolve(pin, out var same, out _));
        Assert.Equal(Path.GetFullPath(pin), same);

        var descendant = Path.Combine(pin, "src", "Project");
        Assert.True(policy.TryResolve(descendant, out var nested, out _));
        Assert.Equal(Path.GetFullPath(descendant), nested);
    }

    [Fact]
    public void Policy_rejects_roots_outside_the_pin()
    {
        var parent = Path.Combine(Path.GetTempPath(), "proof-mcp-scope");
        var pin = Path.Combine(parent, "repo");
        var policy = new McpWorkspacePolicy(pin);

        Assert.False(policy.TryResolve(parent, out _, out var parentError));
        Assert.Contains("outside the pinned workspace root", parentError, StringComparison.Ordinal);

        // 이름이 접두사만 같은 형제는 받아들여지면 안 된다.
        var sibling = Path.Combine(parent, "repo-other");
        Assert.False(policy.TryResolve(sibling, out _, out _));

        var escape = Path.Combine(pin, "..", "repo-other");
        Assert.False(policy.TryResolve(escape, out _, out _));
    }

    [Fact]
    public void Policy_allows_any_root_when_unpinned()
    {
        var policy = new McpWorkspacePolicy(pinnedRoot: null, defaultRoot: null);
        var anywhere = Path.Combine(Path.GetTempPath(), "proof-mcp-elsewhere");

        Assert.True(policy.TryResolve(anywhere, out var resolved, out _));
        Assert.Equal(Path.GetFullPath(anywhere), resolved);
    }

    [Fact]
    public void Policy_rejects_file_paths_outside_the_pin()
    {
        var pin = Path.Combine(Path.GetTempPath(), "proof-mcp-path-pin");
        var policy = new McpWorkspacePolicy(pin);

        Assert.True(policy.TryResolveContained(null, pin, out var none, out _));
        Assert.Null(none);

        Assert.True(policy.TryResolveContained("sub/cert.json", pin, out var relative, out _));
        Assert.Equal(Path.GetFullPath(Path.Combine(pin, "sub", "cert.json")), relative);

        var inside = Path.Combine(pin, "cert.json");
        Assert.True(policy.TryResolveContained(inside, pin, out var absolute, out _));
        Assert.Equal(Path.GetFullPath(inside), absolute);

        Assert.False(policy.TryResolveContained(Path.Combine(pin, "..", "other.json"), pin, out _, out var escapeError));
        Assert.Contains("outside the pinned workspace root", escapeError, StringComparison.Ordinal);

        Assert.False(policy.TryResolveContained(Path.Combine(Path.GetTempPath(), "elsewhere.json"), pin, out _, out _));
    }
}
