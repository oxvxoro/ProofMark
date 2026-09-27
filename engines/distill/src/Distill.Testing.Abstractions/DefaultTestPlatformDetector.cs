namespace Distill.Testing.Abstractions;

public sealed class DefaultTestPlatformDetector : ITestPlatformDetector
{
    public Task<TestPlatformKind> DetectAsync(
        string workspaceRoot,
        string? target,
        CancellationToken cancellationToken,
        string? sourceHint = null)
    {
        _ = workspaceRoot;
        _ = target;
        _ = cancellationToken;
        _ = sourceHint;
        return Task.FromResult(TestPlatformKind.VSTest);
    }
}
