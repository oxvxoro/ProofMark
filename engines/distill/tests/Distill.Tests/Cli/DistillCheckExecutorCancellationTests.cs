using Distill.Execution;
using Distill.Core.Config;
using Distill.Core.Planning;
using Distill.Core.Runs;

namespace Distill.Tests.Cli;

// Brief 02 — 호출자 토큰 취소는 InfraError로 삼켜지거나 다시 매핑되면 안 되고
// ICheckExecutor.ExecuteAsync에서 OperationCanceledException으로 드러나야 한다.
public class DistillCheckExecutorCancellationTests
{
    [Fact]
    public async Task ExecuteAsync_GenericCheck_CallerCancellation_ThrowsOperationCanceled()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"distill-cancel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);

        try
        {
            var context = new DistillRunContext
            {
                RunId = "d-cancel-test",
                WorkspaceRoot = workspace,
                RunDirectory = Path.Combine(workspace, ".distill", "runs", "d-cancel-test"),
                Profile = "quick"
            };
            Directory.CreateDirectory(context.RunDirectory);

            var check = new PlannedCheck(
                "lint",
                new CheckConfig
                {
                    Kind = "lint",
                    Command = "dotnet --version",
                    Timeout = 30
                },
                Array.Empty<string>());

            var executor = new DistillCheckExecutor();

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => executor.ExecuteAsync(check, context, cts.Token));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }
}
