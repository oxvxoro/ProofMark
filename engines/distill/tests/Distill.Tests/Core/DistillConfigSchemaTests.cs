using System.Text.Json;

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
