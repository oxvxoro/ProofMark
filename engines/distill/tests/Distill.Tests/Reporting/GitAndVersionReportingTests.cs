using System.Text.Json;
using Distill.Core.Abstractions;
using Distill.Core.Planning;
using Distill.Core.Redaction;
using Distill.Core.Runs;
using Distill.Git;
using Distill.Reporting;

namespace Distill.Tests.Reporting;

public class GitAndVersionReportingTests
{
    [Fact]
    public void JsonVerificationReporter_Format_IncludesVersionAndAvailableGit()
    {
        var context = CreateContext();
        var gitSnapshot = new GitChangeSnapshot(
            string.Empty,
            Array.Empty<string>(),
            Array.Empty<ChangedHunk>(),
            string.Empty);

        var json = JsonVerificationReporter.Format(
            VerificationStatus.Pass,
            context,
            Array.Empty<CheckRunResult>(),
            Array.Empty<RankedDiagnostic>(),
            new SufficiencyAssessment(true, false, Array.Empty<string>(), Array.Empty<RawExcerpt>()),
            new SecretRedactor(),
            gitSnapshot);

        using var document = JsonDocument.Parse(json);
        Assert.Equal(1, document.RootElement.GetProperty("version").GetInt32());
        Assert.True(document.RootElement.GetProperty("git").GetProperty("available").GetBoolean());
    }

    [Fact]
    public void JsonVerificationReporter_Format_UnavailableGit_IncludesRedactedError()
    {
        var context = CreateContext();
        var gitSnapshot = new GitChangeSnapshot(
            string.Empty,
            Array.Empty<string>(),
            Array.Empty<ChangedHunk>(),
            string.Empty,
            IsAvailable: false,
            ErrorMessage: "token=SECRET123");

        var json = JsonVerificationReporter.Format(
            VerificationStatus.Pass,
            context,
            Array.Empty<CheckRunResult>(),
            Array.Empty<RankedDiagnostic>(),
            new SufficiencyAssessment(true, false, Array.Empty<string>(), Array.Empty<RawExcerpt>()),
            new SecretRedactor(),
            gitSnapshot);

        Assert.DoesNotContain("SECRET123", json);
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.GetProperty("git").GetProperty("available").GetBoolean());
        Assert.Contains("token=***", document.RootElement.GetProperty("git").GetProperty("error").GetString());
    }

    [Fact]
    public async Task WriteNormalizedAsync_IncludesVersionAndGitMetadata()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"distill-normalized-git-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        try
        {
            var gitSnapshot = new GitChangeSnapshot(
                string.Empty,
                Array.Empty<string>(),
                Array.Empty<ChangedHunk>(),
                string.Empty,
                IsAvailable: false,
                ErrorMessage: "Git status command failed.");

            var evidence = new NormalizedEvidence(
                VerificationStatus.Fail,
                Array.Empty<RankedDiagnostic>(),
                Array.Empty<CheckRunResult>(),
                Array.Empty<ChangedHunk>());

            var pipeline = new DistillationPipeline();
            await pipeline.WriteNormalizedAsync(
                evidence,
                workspace,
                gitSnapshot,
                CancellationToken.None,
                new SecretRedactor());

            var json = await File.ReadAllTextAsync(RunArtifactLayout.GetNormalizedPath(workspace));
            using var document = JsonDocument.Parse(json);
            Assert.Equal(1, document.RootElement.GetProperty("version").GetInt32());
            Assert.False(document.RootElement.GetProperty("git").GetProperty("available").GetBoolean());
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public void CompactFailurePackFormatter_UnavailableGit_AddsNote()
    {
        var context = CreateContext();
        var gitSnapshot = new GitChangeSnapshot(
            string.Empty,
            Array.Empty<string>(),
            Array.Empty<ChangedHunk>(),
            string.Empty,
            IsAvailable: false,
            ErrorMessage: "not a git repository");

        var text = CompactFailurePackFormatter.Format(
            VerificationStatus.Pass,
            Array.Empty<CheckRunResult>(),
            Array.Empty<RankedDiagnostic>(),
            context,
            new SufficiencyAssessment(true, false, Array.Empty<string>(), Array.Empty<RawExcerpt>()),
            gitSnapshot: gitSnapshot);

        Assert.Contains("Git evidence: unavailable", text);
        Assert.DoesNotContain("not a git repository", text);
    }

    private static DistillRunContext CreateContext()
        => new()
        {
            RunId = "d-test",
            WorkspaceRoot = "/repo",
            RunDirectory = "/repo/.distill/runs/d-test",
            Profile = "quick"
        };
}
