namespace Distill.Testing.Abstractions;

public interface ITestPlatformDetector
{
    Task<TestPlatformKind> DetectAsync(
        string workspaceRoot,
        string? target,
        CancellationToken cancellationToken,
        string? sourceHint = null);
}
