using Proof.Cli;
using Proof.Core;

namespace Proof.Tests;

public sealed class TestMapYamlEditorTests
{
    private const string CommentedFixture = """
        version: 2

        # top comment survives
        policy:
          publicApiCompatibility: required
          # test maps are authored advice
          testMaps:
            - symbol: "Existing.Symbol"
              # existing test comment
              tests:
                - Proof.Tests.ExistingTests.Method
        """;

    [Fact]
    public void Upsert_NewSymbol_AppendsAfterLastEntry_PreservesComments()
    {
        var updated = TestMapYamlEditor.Upsert(CommentedFixture, "New.Symbol", ["Proof.Tests.NewTests.Method"]);

        Assert.Contains("# top comment survives", updated, StringComparison.Ordinal);
        Assert.Contains("# test maps are authored advice", updated, StringComparison.Ordinal);
        Assert.Contains("# existing test comment", updated, StringComparison.Ordinal);
        var existingIndex = updated.IndexOf("- symbol: \"Existing.Symbol\"", StringComparison.Ordinal);
        var newIndex = updated.IndexOf("- symbol: \"New.Symbol\"", StringComparison.Ordinal);
        Assert.True(existingIndex >= 0 && newIndex > existingIndex);
        Assert.Contains("Proof.Tests.NewTests.Method", updated, StringComparison.Ordinal);
        Assert.EndsWith("\n", updated, StringComparison.Ordinal);
    }

    [Fact]
    public void Upsert_NewSymbol_NoTestMapsBlock_CreatesBlockUnderPolicy()
    {
        const string yaml = """
            policy:
              staticAnalysis: off
            """;

        var updated = TestMapYamlEditor.Upsert(yaml, "New.Symbol", ["Proof.Tests.NewTests.Method"]);

        Assert.Contains("testMaps:", updated, StringComparison.Ordinal);
        Assert.Contains("staticAnalysis: off", updated, StringComparison.Ordinal);
        Assert.Contains("- symbol: \"New.Symbol\"", updated, StringComparison.Ordinal);
    }

    [Fact]
    public void Upsert_NoPolicyBlock_AppendsPolicyBlock()
    {
        const string yaml = "version: 2\n";

        var updated = TestMapYamlEditor.Upsert(yaml, "New.Symbol", ["Proof.Tests.NewTests.Method"]);

        Assert.Contains("policy:", updated, StringComparison.Ordinal);
        Assert.Contains("testMaps:", updated, StringComparison.Ordinal);
        Assert.Contains("Proof.Tests.NewTests.Method", updated, StringComparison.Ordinal);
    }

    [Fact]
    public void Upsert_ExistingSymbol_UnionsTests_KeepsComments()
    {
        var updated = TestMapYamlEditor.Upsert(
            CommentedFixture,
            "Existing.Symbol",
            ["Proof.Tests.ExistingTests.Other", "Proof.Tests.ExistingTests.Method"]);

        Assert.Contains("Proof.Tests.ExistingTests.Method", updated, StringComparison.Ordinal);
        Assert.Contains("Proof.Tests.ExistingTests.Other", updated, StringComparison.Ordinal);
        var first = updated.IndexOf("Proof.Tests.ExistingTests.Method", StringComparison.Ordinal);
        var second = updated.IndexOf("Proof.Tests.ExistingTests.Method", first + 1, StringComparison.Ordinal);
        Assert.Equal(-1, second);
        Assert.Contains("# existing test comment", updated, StringComparison.Ordinal);
    }

    [Fact]
    public void Upsert_DogfoodStyleIndent_UsesEntryIndent()
    {
        // dogfood proof.yml은 공백 2칸인 testMaps 키 아래에 공백 4칸으로 항목을 들여 쓴다
        const string yaml = """
            policy:
              testMaps:
                - symbol: "A.B"
                  tests:
                    - T.One
            """;

        var updated = TestMapYamlEditor.Upsert(yaml, "C.D", ["T.Two"]);

        Assert.Contains("    - symbol: \"C.D\"", updated, StringComparison.Ordinal);
        Assert.Contains("      tests:", updated, StringComparison.Ordinal);
        Assert.Contains("        - T.Two", updated, StringComparison.Ordinal);
        Assert.Contains("- symbol: \"A.B\"", updated, StringComparison.Ordinal);
    }

    [Fact]
    public void Upsert_EmptySymbol_Throws()
    {
        Assert.Throws<ProofConfigException>(() => TestMapYamlEditor.Upsert(CommentedFixture, " ", ["T.X"]));
    }

    [Fact]
    public void Upsert_EmptyTest_Throws()
    {
        Assert.Throws<ProofConfigException>(() => TestMapYamlEditor.Upsert(CommentedFixture, "A.B", ["  "]));
    }

    [Fact]
    public void RenderEntry_QuotesSymbol()
    {
        var rendered = TestMapYamlEditor.RenderEntry(4, "A.B", ["T.X"]);
        Assert.Contains("- symbol: \"A.B\"", rendered, StringComparison.Ordinal);
        Assert.Contains("- T.X", rendered, StringComparison.Ordinal);
    }
}
