using Distill.Core.Abstractions;
using Distill.Core.Planning;
using Distill.Core.Runs;
using Distill.Git;

namespace Distill.Reporting;

public sealed class VerificationReporter : IVerificationReporter
{
    public VerificationPack Build(VerificationRun run, ReportOptions options)
    {
        var context = new DistillRunContext
        {
            RunId = run.RunId,
            WorkspaceRoot = string.Empty,
            RunDirectory = string.Empty,
            Profile = run.Profile
        };

        return VerificationReportBuilder.Build(
            run.Status,
            context,
            run.Checks,
            new GitChangeSnapshot(string.Empty, Array.Empty<string>(), Array.Empty<ChangedHunk>(), string.Empty),
            options);
    }
}
