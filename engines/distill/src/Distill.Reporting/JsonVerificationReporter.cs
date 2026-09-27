using Distill.Core.Abstractions;
using System.Text.Json;
using Distill.Core.Diagnostics;
using Distill.Core.Planning;
using Distill.Core.Redaction;
using Distill.Core.Runs;
using Distill.Git;

namespace Distill.Reporting;

public static class JsonVerificationReporter
{
    public static string Format(
        VerificationStatus status,
        DistillRunContext context,
        IReadOnlyList<CheckRunResult> checks,
        IReadOnlyList<RankedDiagnostic> rankedDiagnostics,
        SufficiencyAssessment sufficiency,
        SecretRedactor? redactor = null,
        GitChangeSnapshot? gitSnapshot = null)
    {
        redactor ??= new SecretRedactor();
        var payload = new
        {
            version = 1,
            status = status.ToString(),
            runId = context.RunId,
            profile = context.Profile,
            runDirectory = context.RunDirectory,
            git = ProjectGit(gitSnapshot, redactor),
            checks = checks.Select(check => new
            {
                check.CheckId,
                check.Kind,
                status = check.Status.ToString(),
                check.ExitCode,
                check.SourceId,
                check.ArtifactPointer,
                durationMs = (int)check.Duration.TotalMilliseconds
            }),
            diagnostics = rankedDiagnostics.Select(item => new
            {
                item.Score,
                reasons = item.Reasons,
                diagnostic = redactor.Redact(item.Diagnostic)
            }),
            sufficiency = new
            {
                sufficiency.IsSufficient,
                sufficiency.ForceUncertain,
                sufficiency.Notes,
                RawExcerpts = sufficiency.RawExcerpts.Select(excerpt => new
                {
                    excerpt.CheckId,
                    excerpt.ArtifactPath,
                    Lines = excerpt.Lines.Select(redactor.Redact).ToList(),
                    excerpt.Reason
                })
            }
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    internal static object ProjectGit(GitChangeSnapshot? gitSnapshot, SecretRedactor redactor)
    {
        gitSnapshot ??= new GitChangeSnapshot(string.Empty, Array.Empty<string>(), Array.Empty<ChangedHunk>(), string.Empty);
        if (gitSnapshot.IsAvailable)
        {
            return new
            {
                available = true,
                changedFileCount = gitSnapshot.ChangedFiles.Count,
                hunkCount = gitSnapshot.Hunks.Count
            };
        }

        return new
        {
            available = false,
            changedFileCount = gitSnapshot.ChangedFiles.Count,
            hunkCount = gitSnapshot.Hunks.Count,
            error = string.IsNullOrWhiteSpace(gitSnapshot.ErrorMessage)
                ? "Git evidence is unavailable."
                : redactor.Redact(gitSnapshot.ErrorMessage)
        };
    }
}

public static class VerificationReportBuilder
{
    public static VerificationPack Build(
        VerificationStatus rawStatus,
        DistillRunContext context,
        IReadOnlyList<CheckRunResult> checks,
        GitChangeSnapshot gitSnapshot,
        ReportOptions options)
    {
        var pipeline = new DistillationPipeline();
        var diagnostics = checks.SelectMany(check => check.Diagnostics).ToList();
        var normalized = pipeline.Normalize(rawStatus, checks, diagnostics, gitSnapshot, context.WorkspaceRoot);
        var initialSufficiency = SufficiencyGuard.Assess(
            rawStatus,
            checks,
            normalized.RankedDiagnostics,
            options.MinConfidence);
        var sufficiency = initialSufficiency with
        {
            RawExcerpts = RawExcerptCollector.Collect(checks, initialSufficiency)
        };
        var redactor = new SecretRedactor(options.RedactionPatterns);
        var status = SufficiencyGuard.Apply(rawStatus, sufficiency);

        var compact = CompactFailurePackFormatter.Format(
            status,
            checks,
            normalized.RankedDiagnostics,
            context,
            sufficiency,
            options.MaxDiagnostics,
            redactor,
            gitSnapshot);

        string? json = null;
        if (options.Format == ReportOutputFormat.Json)
        {
            json = JsonVerificationReporter.Format(
                status,
                context,
                checks,
                normalized.RankedDiagnostics,
                sufficiency,
                redactor,
                gitSnapshot);
        }

        var flatDiagnostics = normalized.RankedDiagnostics
            .Select(item => item.Diagnostic)
            .ToList();

        return new VerificationPack(
            status,
            compact,
            json,
            flatDiagnostics,
            checks,
            context.RunDirectory);
    }
}
