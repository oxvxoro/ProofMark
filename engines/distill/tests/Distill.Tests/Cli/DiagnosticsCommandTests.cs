using Distill.Cli.Commands;
using Distill.Core.Runs;
using System.Text.Json;

namespace Distill.Tests.Cli;

public class DiagnosticsCommandTests
{
    [Fact]
    public void TryResolveNormalizedVersion_MissingVersion_TreatsAsLegacyV1()
    {
        using var document = JsonDocument.Parse("""{"status":"Fail","diagnostics":[]}""");
        Assert.True(DiagnosticsCommand.TryResolveNormalizedVersion(document.RootElement, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void TryResolveNormalizedVersion_VersionOne_IsSupported()
    {
        using var document = JsonDocument.Parse("""{"version":1,"status":"Fail","diagnostics":[]}""");
        Assert.True(DiagnosticsCommand.TryResolveNormalizedVersion(document.RootElement, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void TryResolveNormalizedVersion_UnknownVersion_IsRejected()
    {
        using var document = JsonDocument.Parse("""{"version":99,"status":"Fail","diagnostics":[]}""");
        Assert.False(DiagnosticsCommand.TryResolveNormalizedVersion(document.RootElement, out var error));
        Assert.Contains("Unsupported normalized.json version", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryResolveNormalizedVersion_FractionalVersion_IsRejectedWithoutThrowing()
    {
        using var document = JsonDocument.Parse("""{"version":1.5,"status":"Fail","diagnostics":[]}""");
        Assert.False(DiagnosticsCommand.TryResolveNormalizedVersion(document.RootElement, out var error));
        Assert.Contains("Unsupported normalized.json version", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryResolveNormalizedVersion_OutOfInt32Range_IsRejectedWithoutThrowing()
    {
        using var document = JsonDocument.Parse("""{"version":2147483648,"status":"Fail","diagnostics":[]}""");
        Assert.False(DiagnosticsCommand.TryResolveNormalizedVersion(document.RootElement, out var error));
        Assert.Contains("Unsupported normalized.json version", error, StringComparison.OrdinalIgnoreCase);
    }
}
