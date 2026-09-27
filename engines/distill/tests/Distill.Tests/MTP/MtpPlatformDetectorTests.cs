using Distill.Testing.Abstractions;
using Distill.Testing.MTP;

namespace Distill.Tests.MTP;

public class MtpPlatformDetectorTests
{
    [Fact]
    public async Task DetectAsync_ExplicitMtpSourceWins()
    {
        var workspace = CreateWorkspace();
        try
        {
            var detector = new MtpPlatformDetector();

            var result = await detector.DetectAsync(
                workspace,
                null,
                CancellationToken.None,
                "mtp-report");

            Assert.Equal(TestPlatformKind.Mtp, result);
        }
        finally
        {
            DeleteWorkspace(workspace);
        }
    }

    [Fact]
    public async Task DetectAsync_GlobalJsonRunnerSelectsMtp()
    {
        var workspace = CreateWorkspace();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(workspace, "global.json"),
                """{"test":{"runner":"Microsoft.Testing.Platform"}}""");

            var result = await new MtpPlatformDetector().DetectAsync(
                workspace,
                null,
                CancellationToken.None);

            Assert.Equal(TestPlatformKind.Mtp, result);
        }
        finally
        {
            DeleteWorkspace(workspace);
        }
    }

    [Fact]
    public async Task DetectAsync_VstestHintOverridesGlobalJson()
    {
        var workspace = CreateWorkspace();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(workspace, "global.json"),
                """{"test":{"runner":"Microsoft.Testing.Platform"}}""");

            var result = await new MtpPlatformDetector().DetectAsync(
                workspace,
                null,
                CancellationToken.None,
                "vstest");

            Assert.Equal(TestPlatformKind.VSTest, result);
        }
        finally
        {
            DeleteWorkspace(workspace);
        }
    }

    [Fact]
    public async Task DetectAsync_MtpProjectMarker_SelectsMtp()
    {
        var workspace = CreateWorkspace();
        try
        {
            var projectPath = Path.Combine(workspace, "App.Tests.csproj");
            await File.WriteAllTextAsync(
                projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <EnableMSTestRunner>true</EnableMSTestRunner>
                  </PropertyGroup>
                </Project>
                """);

            var result = await new MtpPlatformDetector().DetectAsync(
                workspace,
                projectPath,
                CancellationToken.None);

            Assert.Equal(TestPlatformKind.Mtp, result);
        }
        finally
        {
            DeleteWorkspace(workspace);
        }
    }

    private static string CreateWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), $"distill-mtp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteWorkspace(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
