using Distill.Core.Evidence;
using Distill.Core.Runs;
using Distill.Runner;
using Distill.Testing.VSTest;

namespace Distill.Tests.VSTest;

public class VstestCompositeResultSourceResolveStatusTests
{
    [Fact]
    public void ResolveStatus_TimedOutWithFailedEvidence_ReturnsInfraError()
    {
        var evidence = EvidenceWithFailedCase();
        var process = new ProcessResult(null, TimeSpan.Zero, ProcessStatus.TimedOut, null, null, "timed out");

        Assert.Equal(
            VerificationStatus.InfraError,
            VstestCompositeResultSource.ResolveStatus(evidence, process));
    }

    [Fact]
    public void ResolveStatus_CanceledWithFailedEvidence_ReturnsInfraError()
    {
        var evidence = EvidenceWithFailedCase();
        var process = new ProcessResult(null, TimeSpan.Zero, ProcessStatus.Canceled, null, null, "canceled");

        Assert.Equal(
            VerificationStatus.InfraError,
            VstestCompositeResultSource.ResolveStatus(evidence, process));
    }

    [Fact]
    public void ResolveStatus_NonZeroExitWithPassingEvidence_IsNotPass()
    {
        var evidence = new TestRunEvidence(
            [new TestCaseEvidence("AlwaysPasses", "Passed", null, null, 1)],
            Passed: 1,
            Failed: 0,
            Skipped: 0,
            Duration: TimeSpan.FromSeconds(1),
            SourceId: "vstest-logger");
        var process = new ProcessResult(2, TimeSpan.Zero, ProcessStatus.Completed, null, null);

        var status = VstestCompositeResultSource.ResolveStatus(evidence, process);

        Assert.NotEqual(VerificationStatus.Pass, status);
        Assert.Equal(VerificationStatus.InfraError, status);
    }

    [Fact]
    public void ResolveStatus_FailedTestWithExitCodeOne_ReturnsFail()
    {
        var evidence = EvidenceWithFailedCase();
        var process = new ProcessResult(1, TimeSpan.Zero, ProcessStatus.Completed, null, null);

        Assert.Equal(
            VerificationStatus.Fail,
            VstestCompositeResultSource.ResolveStatus(evidence, process));
    }

    private static TestRunEvidence EvidenceWithFailedCase()
        => new(
            [new TestCaseEvidence("AlwaysFails", "Failed", "boom", "stack", 1)],
            Passed: 0,
            Failed: 1,
            Skipped: 0,
            Duration: TimeSpan.FromSeconds(1),
            SourceId: "vstest-logger");
}
