using System.Text.Json;
using System.Text.RegularExpressions;
using Distill.Core.Config;

namespace Distill.Tests.Core;

public class DistillConfigSchemaTests
{
    [Fact]
    public void Schema_WorkspaceRunDir_IsConstDefault()
    {
        var schemaPath = ResolveSchemaPath();
        using var document = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var runDir = document.RootElement
            .GetProperty("properties")
            .GetProperty("workspace")
            .GetProperty("properties")
            .GetProperty("runDir");

        Assert.True(runDir.TryGetProperty("const", out var constValue));
        Assert.Equal(".distill/runs", constValue.GetString());
    }

    [Fact]
    public void Schema_ProfileChecks_HasMinItemsOne()
    {
        var schemaPath = ResolveSchemaPath();
        using var document = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var checks = document.RootElement
            .GetProperty("properties")
            .GetProperty("profiles")
            .GetProperty("additionalProperties")
            .GetProperty("properties")
            .GetProperty("checks");

        Assert.True(checks.TryGetProperty("minItems", out var minItems));
        Assert.Equal(1, minItems.GetInt32());
    }

    [Fact]
    public void Schema_CheckPublicFields_MatchRuntimeConfigContract()
    {
        var schemaPath = ResolveSchemaPath();
        using var document = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var properties = document.RootElement
            .GetProperty("properties")
            .GetProperty("checks")
            .GetProperty("additionalProperties")
            .GetProperty("properties");

        var expected = new[]
        {
            "kind", "command", "source", "artifact", "project", "coverage",
            "timeout", "stopOnFailure", "dependsOn"
        };
        Assert.Equal(expected.OrderBy(item => item, StringComparer.Ordinal),
            properties.EnumerateObject().Select(item => item.Name).OrderBy(item => item, StringComparer.Ordinal));
        Assert.Equal(JsonValueKind.Array, properties.GetProperty("artifact").GetProperty("type").ValueKind);
        Assert.Equal(JsonValueKind.Array, properties.GetProperty("project").GetProperty("type").ValueKind);
        Assert.Equal(JsonValueKind.Array, properties.GetProperty("coverage").GetProperty("type").ValueKind);

        var runtime = typeof(Distill.Core.Config.CheckConfig);
        foreach (var property in new[] { "Kind", "Command", "Source", "Artifact", "Project", "Coverage", "Timeout", "StopOnFailure", "DependsOn" })
        {
            Assert.NotNull(runtime.GetProperty(property));
        }
    }

    [Fact]
    public void Schema_Checks_DeclareJUnitProcessRequirements()
    {
        var schemaPath = ResolveSchemaPath();
        using var document = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var checkSchema = document.RootElement
            .GetProperty("properties")
            .GetProperty("checks")
            .GetProperty("additionalProperties");
        var allOf = checkSchema.GetProperty("allOf");

        Assert.True(allOf.GetArrayLength() >= 2);
        Assert.Contains("artifact", allOf.ToString(), StringComparison.Ordinal);
        Assert.Contains("project", allOf.ToString(), StringComparison.Ordinal);
        Assert.Contains("[jJ][uU][nN][iI][tT]", allOf.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Schema_JUnitConditions_MatchRuntimeCaseInsensitiveValues()
    {
        var schemaPath = ResolveSchemaPath();
        using var document = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var allOf = document.RootElement
            .GetProperty("properties")
            .GetProperty("checks")
            .GetProperty("additionalProperties")
            .GetProperty("allOf");

        Assert.Equal("^[pP][rR][oO][cC][eE][sS][sS]$",
            allOf[0].GetProperty("if").GetProperty("properties").GetProperty("kind").GetProperty("pattern").GetString());
        Assert.Equal("^[jJ][uU][nN][iI][tT]$",
            allOf[0].GetProperty("if").GetProperty("properties").GetProperty("source").GetProperty("pattern").GetString());

        const string yaml = """
            version: 1
            profiles:
              quick:
                checks: [web]
            checks:
              web:
                kind: PROCESS
                command: npm test
                source: JUNIT
                artifact: report.xml
                project: scip:web
            """;

        var config = DistillConfigLoader.LoadFromYaml(yaml);
        Assert.Equal("JUNIT", config.Checks["web"].Source);
    }

    [Fact]
    public void Schema_NullOptionalProcessArtifacts_MatchRuntimeValidation()
    {
        var schemaPath = ResolveSchemaPath();
        using var document = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var checkSchema = document.RootElement
            .GetProperty("properties")
            .GetProperty("checks")
            .GetProperty("additionalProperties");
        var nonProcessForbidden = checkSchema.GetProperty("allOf")[1]
            .GetProperty("else").GetProperty("not").GetProperty("anyOf");
        foreach (var property in new[] { "artifact", "project", "coverage" })
        {
            var clause = Assert.Single(nonProcessForbidden.EnumerateArray(), item =>
                item.GetProperty("required")[0].GetString() == property);
            Assert.Equal("string", clause.GetProperty("properties").GetProperty(property).GetProperty("type").GetString());
        }

        const string yaml = """
            version: 1
            profiles:
              quick:
                checks: [unit]
            checks:
              unit:
                kind: test
                command: dotnet test App.Tests.csproj
                artifact: null
                project: null
                coverage: null
            """;

        var config = DistillConfigLoader.LoadFromYaml(yaml);
        Assert.Null(config.Checks["unit"].Artifact);
        Assert.Null(config.Checks["unit"].Project);
        Assert.Null(config.Checks["unit"].Coverage);
    }

    [Fact]
    public void GeneratedContractSurface_MatchesSourceContracts()
    {
        var root = FindRepositoryRoot();
        var versionMatch = Regex.Match(
            File.ReadAllText(Path.Combine(root, "Version.props")),
            "<VersionPrefix>([^<]+)</VersionPrefix>");
        Assert.True(versionMatch.Success, "Version.props must define VersionPrefix.");
        var version = versionMatch.Groups[1].Value;

        var planningDirectory = Path.Combine(root, "src", "Proof.Engine", "Planning");
        var ruleIds = Directory.GetFiles(planningDirectory, "*.cs")
            .SelectMany(path => Regex.Matches(File.ReadAllText(path), "\\\"(P\\d{3}[A-Z]?)\\\"")
                .Select(match => match.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        var reasonCodes = Regex.Matches(
                File.ReadAllText(Path.Combine(root, "src", "Proof.Core", "Policy.cs")),
                "public const string \\w+ = \\\"([A-Z][A-Z0-9_]+)\\\";")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        var tools = Regex.Matches(
                File.ReadAllText(Path.Combine(root, "src", "Proof.Cli", "Mcp", "ProofMcpToolCatalog.cs")),
                "internal const string \\w+ = \\\"(proof_[a-z_]+)\\\";")
            .Select(match => match.Groups[1].Value)
            .Order(StringComparer.Ordinal);

        var checkFields = typeof(CheckConfig).GetProperties()
            .Select(property => property.Name)
            .ToArray();
        var schemaPath = ResolveSchemaPath();
        using (var schema = JsonDocument.Parse(File.ReadAllText(schemaPath)))
        {
            var schemaFields = schema.RootElement.GetProperty("properties")
                .GetProperty("checks").GetProperty("additionalProperties")
                .GetProperty("properties").EnumerateObject()
                .Select(property => char.ToUpperInvariant(property.Name[0]) + property.Name[1..])
                .ToHashSet(StringComparer.Ordinal);
            Assert.Equal(checkFields.ToHashSet(StringComparer.Ordinal), schemaFields);
        }

        var expected = string.Join('\n',
        [
            "# 생성된 공개 계약 표",
            "",
            "> CI가 소스 정의와 비교하는 요약이다. 계약 설명의 유일한 출처가 아니며, 의미 문서는 별도 검토한다.",
            "",
            $"- 도구 패키지 버전: `{version}` (`Version.props`)",
            $"- planner 규칙 ID: {string.Join(", ", ruleIds.Select(id => $"`{id}`"))}",
            $"- Proof 이유 코드: {string.Join(", ", reasonCodes.Select(code => $"`{code}`"))}",
            $"- Proof MCP 도구: {string.Join(", ", tools.Select(tool => $"`{tool}`"))}",
            $"- Distill check 필드: {string.Join(", ", checkFields.Select(field => $"`{field}`"))}",
            ""
        ]);
        var surfacePath = Path.Combine(root, "docs", "generated", "contract-surface.md");
        Assert.True(File.Exists(surfacePath), "Generated contract surface is missing.");
        Assert.Equal(expected, File.ReadAllText(surfacePath).Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "Version.props"))
                && Directory.Exists(Path.Combine(current.FullName, "src", "Proof.Engine", "Planning")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the Proofmark repository root from the test output directory.");
    }

    private static string ResolveSchemaPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "schemas", "distill-config-v1.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate schemas/distill-config-v1.json from test output directory.");
    }
}
