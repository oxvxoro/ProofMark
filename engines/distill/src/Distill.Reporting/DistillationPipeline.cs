using System.Text.Json;
using System.Text.Json.Serialization;
using Distill.Core.Diagnostics;
using Distill.Core.Planning;
using Distill.Core.Redaction;
using Distill.Core.Runs;
using Distill.Git;

namespace Distill.Reporting;

public sealed record NormalizedEvidence(
    VerificationStatus Status,
    IReadOnlyList<RankedDiagnostic> RankedDiagnostics,
    IReadOnlyList<CheckRunResult> Checks,
    IReadOnlyList<ChangedHunk> ChangedHunks);

public sealed class DistillationPipeline
{
    public NormalizedEvidence Normalize(
        VerificationStatus status,
        IReadOnlyList<CheckRunResult> checks,
        IReadOnlyList<DistillDiagnostic> diagnostics,
        GitChangeSnapshot gitSnapshot,
        string? workspaceRoot = null)
    {
        var ranked = EvidenceCorrelator.Rank(
            diagnostics,
            gitSnapshot.Hunks,
            gitSnapshot.ChangedFiles,
            workspaceRoot);

        return new NormalizedEvidence(status, ranked, checks, gitSnapshot.Hunks);
    }

    public async Task WriteNormalizedAsync(
        NormalizedEvidence evidence,
        string runDirectory,
        GitChangeSnapshot gitSnapshot,
        CancellationToken cancellationToken,
        SecretRedactor? redactor = null)
    {
        redactor ??= new SecretRedactor();
        var payload = new
        {
            version = 1,
            status = evidence.Status.ToString(),
            git = JsonVerificationReporter.ProjectGit(gitSnapshot, redactor),
            checks = evidence.Checks.Select(check => new
            {
                check.CheckId,
                status = check.Status.ToString(),
                check.ExitCode,
                check.SourceId,
                check.ArtifactPointer
            }),
            diagnostics = evidence.RankedDiagnostics.Select(item => new
            {
                item.Score,
                item.Reasons,
                Diagnostic = redactor.Redact(item.Diagnostic)
            })
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        });

        var path = RunArtifactLayout.GetNormalizedPath(runDirectory);
        await AtomicFileWriter.WriteTextAsync(path, json, cancellationToken).ConfigureAwait(false);
    }
}
