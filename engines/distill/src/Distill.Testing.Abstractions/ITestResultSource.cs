using Distill.Core.Evidence;
using Distill.Core.Runs;

namespace Distill.Testing.Abstractions;

public interface ITestResultSource
{
    string Id { get; }

    bool CanHandle(TestPlatformKind platform);

    Task<TestRunEvidence> ExecuteAndCollectAsync(
        TestCheckDefinition check,
        DistillRunContext context,
        CancellationToken cancellationToken);
}
