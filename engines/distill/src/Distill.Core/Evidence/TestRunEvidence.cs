namespace Distill.Core.Evidence;

public sealed record TestRunEvidence(
    IReadOnlyList<TestCaseEvidence> Cases,
    int Passed,
    int Failed,
    int Skipped,
    TimeSpan Duration,
    string SourceId);
