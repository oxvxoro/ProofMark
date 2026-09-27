namespace Distill.Execution;

public static class DistillCheckExecutorFactory
{
    public static DistillCheckExecutor Create(
        string? loggerExtensionDirectory = null,
        string configuredPlatform = "auto") =>
        new(loggerExtensionDirectory, configuredPlatform);
}
