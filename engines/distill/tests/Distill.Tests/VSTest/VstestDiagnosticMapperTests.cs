using Distill.Core.Diagnostics;
using Distill.Core.Evidence;
using Distill.Testing.VSTest;

namespace Distill.Tests.VSTest;

public class VstestDiagnosticMapperTests
{
    [Fact]
    public void ParseStackFrames_ParsesFileLineAndFrameworkFlag()
    {
        const string stack = """
            at SampleTests.AlwaysFails() in C:\repo\SampleTests.cs:line 10
            at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
            """;

        var frames = VstestDiagnosticMapper.ParseStackFrames(stack);

        Assert.Equal(2, frames.Count);
        Assert.False(frames[0].IsFramework);
        Assert.Equal("C:/repo/SampleTests.cs", frames[0].File);
        Assert.Equal(10, frames[0].Line);
        Assert.True(frames[1].IsFramework);
    }

    [Fact]
    public void MapFailedCase_UsesTestNameAndMessage()
    {
        var testCase = new TestCaseEvidence(
            "SampleTests.AlwaysFails",
            "failed",
            "Assert.True() Failure",
            "at SampleTests.AlwaysFails() in C:/repo/SampleTests.cs:line 10",
            5.5);

        var diagnostic = VstestDiagnosticMapper.MapFailedCase(
            testCase,
            DiagnosticProvenance.VSTestLoggerEvent,
            1.0,
            1);

        Assert.Equal("SampleTests.AlwaysFails", diagnostic.TestName);
        Assert.Equal(DiagnosticKind.Test, diagnostic.Kind);
        Assert.NotEmpty(diagnostic.Frames);
    }
}
