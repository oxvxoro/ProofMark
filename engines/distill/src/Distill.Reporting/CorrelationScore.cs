namespace Distill.Reporting;

public static class CorrelationScore
{
    public const int DiagnosticLineInsideChangedHunk = 100;
    public const int StackFrameInsideChangedHunk = 90;
    public const int DiagnosticInChangedFile = 60;
    public const int FailedTestFileChanged = 50;
    public const int DirectCompilerError = 50;
    public const int FailedAssertion = 45;
    public const int Warning = -20;
    public const int DuplicateInstance = -30;
    public const int FrameworkOnlyFrame = -60;
    public const int GeneratedFile = -40;
}
