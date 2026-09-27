using System.CommandLine;
using System.Text.Json;
using Proof.Core;
using Proof.Engine;

namespace Proof.Cli;

public static class PlanCommand
{
    public static Command Create()
    {
        var baseOption = new Option<string?>("--base")
        {
            Description = "Git revision to compare against."
        };

        var command = new Command("plan", "Show the change set, impact, and obligations without running Distill checks.")
        {
            baseOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var baseRevision = parseResult.GetValue(baseOption);
            return await ExecuteAsync(baseRevision, cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    internal static async Task<int> ExecuteAsync(string? baseRevision, CancellationToken cancellationToken)
    {
        try
        {
            var workspace = await ProofWorkspace.PlanAsync(baseRevision, profile: null, cancellationToken).ConfigureAwait(false);
            var snapshot = workspace.Snapshot;
            var impact = workspace.Impact;
            var plan = workspace.Plan;
            var verificationPlan = workspace.VerificationPlan;

            Console.WriteLine(JsonSerializer.Serialize(new
            {
                snapshot.BaseCommitSha,
                snapshot.HeadCommitSha,
                snapshot.SourceDigest,
                snapshot.ChangeSetIsEmpty,
                Files = snapshot.Files,
                Spans = snapshot.Files.SelectMany(file => file.NewSpans),
                ChangedSymbols = impact.ChangedSymbols,
                Obligations = plan.Obligations,
                Constraints = plan.Constraints,
                Uncovered = verificationPlan?.Uncovered
            }, ProofJson.WireOptions));
            return 0;
        }
        catch (ProofConfigException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }
}
