using Proof.Cli;
using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

/// <summary>
/// PR-13 메트릭 사이드카: 오케스트레이터가 실행의 단계 시간과 횟수를
/// 기록하고, CLI는 서명된 statement를 건드리지 않은 채
/// .proof/runs/&lt;runId&gt;/metrics.json에 그것을 쓴다.
/// </summary>
public sealed class VerificationMetricsTests
{
    [Fact]
    public async Task VerifyAsync_WithMetricsSink_RecordsPhaseCounts()
    {
        var collector = new ProofMetricsCollector();
        var orchestrator = new ProofOrchestrator(
            new OnePublicSymbolProvider(),
            new DeterministicProofPlanner(),
            new EmptyRunner(),
            new DeterministicProofEvaluator(),
            new ChangeCertificateBuilder(),
            metricsSink: collector);

        var request = new ChangeRequest("root", "base", "head", [], SourceDigest: "src");
        var certificate = await orchestrator.VerifyAsync(request, "quick", CancellationToken.None);

        var metrics = collector.Last;
        Assert.NotNull(metrics);
        Assert.Equal(certificate.Plan.Obligations.Count, metrics!.Counts.Obligations);
        Assert.Equal(certificate.Impact.ChangedSymbols.Count, metrics.Counts.ChangedSymbols);
        Assert.Equal(certificate.Evidence.Evidence.Count, metrics.Counts.Evidence);
        Assert.Equal(certificate.Evidence.Links.Count, metrics.Counts.Links);
        Assert.True(metrics.ImpactMs >= 0);
        Assert.True(metrics.PlanningMs >= 0);
        Assert.True(metrics.BindingMs >= 0);
        Assert.True(metrics.EvaluationMs >= 0);
        Assert.True(metrics.CertificateMs >= 0);
    }

    [Fact]
    public async Task VerifyAsync_WithoutMetricsSink_StillProducesCertificate()
    {
        var orchestrator = new ProofOrchestrator(
            new OnePublicSymbolProvider(),
            new DeterministicProofPlanner(),
            new EmptyRunner(),
            new DeterministicProofEvaluator(),
            new ChangeCertificateBuilder());

        var request = new ChangeRequest("root", "base", "head", [], SourceDigest: "src");
        var certificate = await orchestrator.VerifyAsync(request, "quick", CancellationToken.None);

        Assert.NotNull(certificate.StatementDigest);
        Assert.NotNull(certificate.CertificateDigest);
    }

    [Fact]
    public void WriteMetrics_WritesCamelCaseSidecarUnderRunDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-metrics-" + Guid.NewGuid().ToString("N"));
        try
        {
            var metrics = new VerificationMetrics(
                SnapshotMs: 1.5,
                ImpactMs: 2,
                PlanningMs: 3,
                VerificationMs: 4,
                BindingMs: 5,
                EvaluationMs: 6,
                CertificateMs: 7,
                new VerificationMetricCounts(1, 2, 3, 4, 5, 6));

            var path = VerifyCommand.WriteMetrics(root, "run-123", metrics);

            Assert.Equal(Path.Combine(root, ".proof", "runs", "run-123", "metrics.json"), path);
            Assert.NotNull(path);
            Assert.True(File.Exists(path));
            var json = File.ReadAllText(path!);
            Assert.Contains("\"snapshotMs\": 1.5", json);
            Assert.Contains("\"changedSymbols\": 1", json);
            Assert.Contains("\"links\": 6", json);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void WriteMetrics_SkipsWhenNoMetricsOrRunId()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-metrics-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Null(VerifyCommand.WriteMetrics(root, null, new VerificationMetrics(0, 0, 0, 0, 0, 0, 0, new VerificationMetricCounts(0, 0, 0, 0, 0, 0))));
            Assert.Null(VerifyCommand.WriteMetrics(root, "run", null));
            Assert.False(Directory.Exists(root));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class OnePublicSymbolProvider : IChangeImpactProvider
    {
        public Task<ChangeImpact> AnalyzeAsync(ChangeRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ChangeImpact(
                request.BaseRevision,
                request.HeadRevision,
                request.Spans,
                [new ChangedSymbolRef("pub-1", "App", "App/Legacy.cs", "Legacy.Do", 1, 5, IsPublic: true, IsTest: false)],
                [],
                ["App"],
                [],
                "complete",
                false,
                SourceDigest: request.SourceDigest));
    }

    private sealed class EmptyRunner : IVerificationRunner
    {
        public Task<VerificationRunResult> VerifyAsync(
            ProofPlan plan,
            VerificationPlan verificationPlan,
            string workspaceRoot,
            CancellationToken cancellationToken)
            => Task.FromResult(new VerificationRunResult(new VerificationEvidenceSet([], []), []));
    }
}
