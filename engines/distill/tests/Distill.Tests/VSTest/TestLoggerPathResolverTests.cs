using Distill.Testing.VSTest;

namespace Distill.Tests.VSTest;

public class TestLoggerPathResolverTests
{
    [Fact]
    public void ResolveExtensionDirectory_FindsDistillTestLogger()
    {
        var directory = TestLoggerPathResolver.ResolveExtensionDirectory();

        Assert.True(File.Exists(Path.Combine(directory, "Distill.TestLogger.dll")));
    }
}
