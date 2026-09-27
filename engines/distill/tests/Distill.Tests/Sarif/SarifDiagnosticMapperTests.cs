using Distill.Analysis.Sarif;
using Distill.Core.Diagnostics;

namespace Distill.Tests.Sarif;

public class SarifDiagnosticMapperTests
{
    [Fact]
    public void Map_ExtractsRuleSeverityToolAndLocation()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "fixtures", "sarif", "single-warning.sarif"));

        var diagnostics = SarifDiagnosticMapper.Map(path);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticKind.Analysis, diagnostic.Kind);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("DEMO001", diagnostic.Code);
        Assert.Equal("DemoAnalyzer", diagnostic.Source);
        Assert.Equal("src/Demo.cs", diagnostic.Location?.File);
        Assert.Equal(12, diagnostic.Location?.Line);
        Assert.Equal(DiagnosticProvenance.Sarif, diagnostic.Provenance);
    }

    [Fact]
    public void Map_ExtractsProjectFromRunProperties()
    {
        var json = """
        {
          "version": "2.1.0",
          "runs": [
            {
              "properties": { "project": "Proof.Core" },
              "results": [
                {
                  "ruleId": "DEMO001",
                  "level": "warning",
                  "message": { "text": "demo" }
                }
              ]
            }
          ]
        }
        """;

        using var document = System.Text.Json.JsonDocument.Parse(json);
        var diagnostics = SarifDiagnosticMapper.Map(document.RootElement);

        Assert.All(diagnostics, diagnostic => Assert.Equal("Proof.Core", diagnostic.Project));
    }

    [Fact]
    public void Map_ResultProjectOverridesRunProject()
    {
        var json = """
        {
          "version": "2.1.0",
          "runs": [
            {
              "properties": { "project": "Run.Project" },
              "results": [
                {
                  "ruleId": "DEMO001",
                  "level": "warning",
                  "message": { "text": "demo" },
                  "properties": { "project": "Result.Project" }
                }
              ]
            }
          ]
        }
        """;

        using var document = System.Text.Json.JsonDocument.Parse(json);
        var diagnostics = SarifDiagnosticMapper.Map(document.RootElement);

        Assert.Equal("Result.Project", Assert.Single(diagnostics).Project);
    }

    [Fact]
    public void Map_FallsBackToAutomationDetailsId()
    {
        var json = """
        {
          "version": "2.1.0",
          "runs": [
            {
              "automationDetails": { "id": "analyzers/Proof.Adapters.CodeMap" },
              "results": [
                {
                  "ruleId": "DEMO001",
                  "level": "warning",
                  "message": { "text": "demo" }
                }
              ]
            }
          ]
        }
        """;

        using var document = System.Text.Json.JsonDocument.Parse(json);
        var diagnostics = SarifDiagnosticMapper.Map(document.RootElement);

        // 프로젝트는 자동화 id의 마지막 구간이며, 검사
        // 접두사가 아니다. 검사 id를 프로젝트 이름으로 오인해서는 안 된다.
        Assert.Equal("Proof.Adapters.CodeMap", Assert.Single(diagnostics).Project);
    }

    [Fact]
    public void Map_MissingProjectLeavesProjectNull()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "fixtures", "sarif", "single-warning.sarif"));

        var diagnostics = SarifDiagnosticMapper.Map(path);

        Assert.Null(Assert.Single(diagnostics).Project);
    }
}
