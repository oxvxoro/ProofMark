namespace Distill.Core.Config;

public sealed class DistillConfig
{
    public int Version { get; set; } = 1;

    public WorkspaceConfig Workspace { get; set; } = new();

    public TestingConfig Testing { get; set; } = new();

    public RedactionConfig Redaction { get; set; } = new();

    public ReliabilityConfig Reliability { get; set; } = new();

    public Dictionary<string, ProfileDefinition> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, CheckConfig> Checks { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class WorkspaceConfig
{
    public string? Solution { get; set; }

    public string RunDir { get; set; } = ".distill/runs";
}

public sealed class TestingConfig
{
    public string Platform { get; set; } = "auto";
}

public sealed class RedactionConfig
{
    public List<string> Patterns { get; set; } = new();
}

public sealed class ReliabilityConfig
{
    public double MinConfidence { get; set; } = 0.5;
}

public sealed class ProfileDefinition
{
    public List<string> Checks { get; set; } = new();
}

public sealed class CheckConfig
{
    public required string Kind { get; set; }

    public required string Command { get; set; }

    public string Source { get; set; } = "auto";

    public int Timeout { get; set; } = 300;

    public bool StopOnFailure { get; set; } = true;

    public List<string> DependsOn { get; set; } = new();

    // kind: process + source: junit 전용. 명령이 쓰는 JUnit XML 경로(작업 공간 기준)다.
    public string? Artifact { get; set; }

    // JUnit 테스트 케이스의 프로젝트. scip:{name} 또는 프로젝트 이름/경로다.
    public string? Project { get; set; }

    // kind: process 전용. 명령이 쓰는 Cobertura XML 경로(작업 공간 기준)다.
    public string? Coverage { get; set; }
}
