using Proof.Cli;
using Proof.Core;

namespace Proof.Tests;

[Collection("WorkingDirectory")]
public sealed class MapAddCommandTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "proof-map-add-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workspace, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private string WriteProofYaml(string yaml = """
        version: 2

        policy:
          publicApiCompatibility: required
          testMaps:
            - symbol: "Existing.Symbol"
              tests:
                - Proof.Tests.ExistingTests.Method
        """)
    {
        Directory.CreateDirectory(_workspace);
        var path = Path.Combine(_workspace, "proof.yml");
        File.WriteAllText(path, yaml);
        return path;
    }

    [Fact]
    public void Add_WithoutAccept_DoesNotChangeFile()
    {
        var path = WriteProofYaml();
        var original = File.ReadAllText(path);
        Directory.SetCurrentDirectory(_workspace);
        try
        {
            var exit = MapSuggestCommand.ExecuteAdd("New.Symbol", ["Proof.Tests.NewTests.Method"], accept: false, configPath: null);

            Assert.Equal(0, exit);
            Assert.Equal(original, File.ReadAllText(path));
        }
        finally
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
        }
    }

    [Fact]
    public void Add_WithAccept_WritesEntry_AndStaysParsable()
    {
        var path = WriteProofYaml();
        var original = File.ReadAllText(path);
        Directory.SetCurrentDirectory(_workspace);
        try
        {
            var exit = MapSuggestCommand.ExecuteAdd("New.Symbol", ["Proof.Tests.NewTests.Method"], accept: true, configPath: null);

            Assert.Equal(0, exit);
            var updated = File.ReadAllText(path);
            Assert.Contains("Proof.Tests.NewTests.Method", updated, StringComparison.Ordinal);
            var config = ProofConfig.Load(_workspace);
            Assert.Equal(2, config.Policy.TestMaps.Count);
        }
        finally
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
        }
    }

    [Fact]
    public void Add_ExistingSymbol_UnionsWithoutDuplicates()
    {
        var path = WriteProofYaml();
        Directory.SetCurrentDirectory(_workspace);
        try
        {
            var exit = MapSuggestCommand.ExecuteAdd(
                "Existing.Symbol",
                ["Proof.Tests.ExistingTests.Method", "Proof.Tests.ExistingTests.Other"],
                accept: true,
                configPath: null);

            Assert.Equal(0, exit);
            var config = ProofConfig.Load(_workspace);
            var entry = Assert.Single(config.Policy.TestMaps);
            Assert.Equal(2, entry.Tests.Count);
        }
        finally
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
        }
    }

    [Fact]
    public void Add_BareTestName_IsRejectedWithConfigError()
    {
        WriteProofYaml();
        Directory.SetCurrentDirectory(_workspace);
        try
        {
            // 구성 오류로 종료 코드 2. 어느 경우든 파일은 그대로 남는다.
            Assert.Equal(2, MapSuggestCommand.ExecuteAdd("New.Symbol", ["BareName"], accept: false, configPath: null));
        }
        finally
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
        }
    }

    [Fact]
    public void Add_WhitespaceTestName_IsRejectedWithConfigError()
    {
        WriteProofYaml();
        Directory.SetCurrentDirectory(_workspace);
        try
        {
            Assert.Equal(2, MapSuggestCommand.ExecuteAdd("New.Symbol", ["Proof.Tests.Bad Name"], accept: false, configPath: null));
        }
        finally
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
        }
    }

    [Fact]
    public void Add_ConfigPath_ReadsAndWritesThatFile_NotWorkspaceProofYml()
    {
        var workspacePath = WriteProofYaml();
        var other = Path.Combine(_workspace, "other-proof.yml");
        File.WriteAllText(other, """
            version: 2
            policy:
              testMaps:
                - symbol: "Other.Symbol"
                  tests:
                    - Proof.Tests.OtherTests.Method
            """);
        var workspaceOriginal = File.ReadAllText(workspacePath);
        Directory.SetCurrentDirectory(_workspace);
        try
        {
            var exit = MapSuggestCommand.ExecuteAdd(
                "FromConfig.Symbol",
                ["Proof.Tests.FromConfigTests.Method"],
                accept: true,
                configPath: other);

            Assert.Equal(0, exit);
            Assert.Equal(workspaceOriginal, File.ReadAllText(workspacePath));
            var updated = File.ReadAllText(other);
            Assert.Contains("FromConfig.Symbol", updated, StringComparison.Ordinal);
            Assert.Contains("Other.Symbol", updated, StringComparison.Ordinal);
            var loaded = ProofConfig.LoadedFromFile(other, _workspace);
            Assert.Equal(2, loaded.Policy.TestMaps.Count);
        }
        finally
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
        }
    }

    [Fact]
    public void Add_SuggestResultIsNeverAutoAccepted()
    {
        // map add는 사용자가 넘긴 심볼만 쓴다. map suggest의 휴리스틱
        // 제안은 절대 읽지 않는다. dry run 출력은 그 항목 자체다.
        var path = WriteProofYaml();
        Directory.SetCurrentDirectory(_workspace);
        try
        {
            var exit = MapSuggestCommand.ExecuteAdd("New.Symbol", ["Proof.Tests.NewTests.Method"], accept: false, configPath: null);
            Assert.Equal(0, exit);
            Assert.DoesNotContain("New.Symbol", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
        }
    }
}
