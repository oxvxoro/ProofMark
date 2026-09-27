using Proof.Core;
using Proof.Engine;

namespace Proof.Tests;

public sealed class EvidenceProducerPipelineTests
{
    [Fact]
    public async Task VerifyAsync_BeforeProducer_RunsWhileRunnerIsStillRunning()
    {
        var runner = new GatedRunner();
        var producer = new SignalingProducer("before", order: 0, EvidenceProductionStage.BeforeVerification);
        var orchestrator = BuildOrchestrator(runner, [producer]);

        var verifyTask = orchestrator.VerifyAsync(Request(), "quick", CancellationToken.None);

        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await producer.Produced.Task.WaitAsync(TimeSpan.FromSeconds(10));
        runner.Release();
        var certificate = await verifyTask;

        Assert.True(producer.Produced.Task.IsCompleted);
        Assert.Contains(certificate.Evidence.Evidence, item => item.Id == "before");
    }

    [Fact]
    public async Task VerifyAsync_ProducerEvidence_IsOrderedByOrderRegardlessOfCompletion()
    {
        var slow = new SignalingProducer("slow", order: 0, EvidenceProductionStage.BeforeVerification, delayMs: 80);
        var fast = new SignalingProducer("fast", order: 1, EvidenceProductionStage.BeforeVerification);
        var orchestrator = BuildOrchestrator(new ImmediateRunner(), [fast, slow]);

        var certificate = await orchestrator.VerifyAsync(Request(), "quick", CancellationToken.None);

        Assert.Equal(["slow", "fast"], certificate.Evidence.Evidence.Select(item => item.Id));
    }

    [Fact]
    public async Task VerifyAsync_AfterProducer_RunsAfterRunnerCompletes()
    {
        var events = new List<string>();
        var runner = new EventRunner(events);
        var after = new EventProducer("after", order: 0, EvidenceProductionStage.AfterVerification, events);
        var orchestrator = BuildOrchestrator(runner, [after]);

        await orchestrator.VerifyAsync(Request(), "quick", CancellationToken.None);

        Assert.Equal(["runner", "after"], events);
    }

    [Fact]
    public async Task VerifyAsync_AfterProducerReceivesVerificationRun()
    {
        var after = new RunInspectingProducer();
        var orchestrator = BuildOrchestrator(new ImmediateRunner(), [after]);

        await orchestrator.VerifyAsync(Request(), "quick", CancellationToken.None);

        Assert.True(after.SawVerificationRun);
    }

    [Fact]
    public async Task VerifyAsync_EmptyProducerList_StillVerifies()
    {
        var orchestrator = BuildOrchestrator(new ImmediateRunner(), []);

        var certificate = await orchestrator.VerifyAsync(Request(), "quick", CancellationToken.None);

        Assert.NotNull(certificate);
        Assert.Empty(certificate.Evidence.Evidence);
    }

    [Fact]
    public async Task VerifyAsync_ProducerException_Propagates()
    {
        var orchestrator = BuildOrchestrator(new ImmediateRunner(), [new ThrowingProducer()]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => orchestrator.VerifyAsync(Request(), "quick", CancellationToken.None));
    }

    private static ProofOrchestrator BuildOrchestrator(
        IVerificationRunner runner,
        IReadOnlyList<IEvidenceProducer> producers)
        => new(
            new StaticImpactProvider(),
            new DeterministicProofPlanner(),
            runner,
            new DeterministicProofEvaluator(),
            new ChangeCertificateBuilder(),
            evidenceProducers: producers);

    private static ChangeRequest Request() =>
        new("root", "base", "head", [], ChangeSetIsEmpty: true, SourceDigest: "src");

    private static ProofEvidence Evidence(string id) =>
        new(id, EvidenceKind.TestRun, id, EvidenceStatus.Pass, new EvidenceProvenance("test"));

    private sealed class GatedRunner : IVerificationRunner
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _gate.TrySetResult();

        public async Task<VerificationRunResult> VerifyAsync(
            ProofPlan plan,
            VerificationPlan verificationPlan,
            string workspaceRoot,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await _gate.Task.ConfigureAwait(false);
            return new VerificationRunResult(new VerificationEvidenceSet([], []), []);
        }
    }

    private sealed class ImmediateRunner : IVerificationRunner
    {
        public Task<VerificationRunResult> VerifyAsync(
            ProofPlan plan,
            VerificationPlan verificationPlan,
            string workspaceRoot,
            CancellationToken cancellationToken)
            => Task.FromResult(new VerificationRunResult(new VerificationEvidenceSet([], []), []));
    }

    private sealed class EventRunner(List<string> events) : IVerificationRunner
    {
        public Task<VerificationRunResult> VerifyAsync(
            ProofPlan plan,
            VerificationPlan verificationPlan,
            string workspaceRoot,
            CancellationToken cancellationToken)
        {
            lock (events)
            {
                events.Add("runner");
            }

            return Task.FromResult(new VerificationRunResult(new VerificationEvidenceSet([], []), []));
        }
    }

    private sealed class SignalingProducer : IEvidenceProducer
    {
        private readonly int _delayMs;

        public SignalingProducer(
            string name,
            int order,
            EvidenceProductionStage stage,
            int delayMs = 0)
        {
            Name = name;
            Order = order;
            Stage = stage;
            _delayMs = delayMs;
        }

        public string Name { get; }

        public int Order { get; }

        public EvidenceProductionStage Stage { get; }

        public TaskCompletionSource Produced { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<IReadOnlyList<ProofEvidence>> ProduceAsync(
            EvidenceProductionContext context,
            CancellationToken cancellationToken)
        {
            if (_delayMs > 0)
            {
                await Task.Delay(_delayMs, cancellationToken).ConfigureAwait(false);
            }

            Produced.TrySetResult();
            return [Evidence(Name)];
        }
    }

    private sealed class EventProducer : IEvidenceProducer
    {
        private readonly List<string> _events;

        public EventProducer(string name, int order, EvidenceProductionStage stage, List<string> events)
        {
            Name = name;
            Order = order;
            Stage = stage;
            _events = events;
        }

        public string Name { get; }

        public int Order { get; }

        public EvidenceProductionStage Stage { get; }

        public ValueTask<IReadOnlyList<ProofEvidence>> ProduceAsync(
            EvidenceProductionContext context,
            CancellationToken cancellationToken)
        {
            lock (_events)
            {
                _events.Add(Name);
            }

            return ValueTask.FromResult<IReadOnlyList<ProofEvidence>>([Evidence(Name)]);
        }
    }

    private sealed class RunInspectingProducer : IEvidenceProducer
    {
        public string Name => "inspect";

        public int Order => 0;

        public EvidenceProductionStage Stage => EvidenceProductionStage.AfterVerification;

        public bool SawVerificationRun { get; private set; }

        public ValueTask<IReadOnlyList<ProofEvidence>> ProduceAsync(
            EvidenceProductionContext context,
            CancellationToken cancellationToken)
        {
            SawVerificationRun = context.VerificationRun is not null;
            return ValueTask.FromResult<IReadOnlyList<ProofEvidence>>([]);
        }
    }

    private sealed class ThrowingProducer : IEvidenceProducer
    {
        public string Name => "throwing";

        public int Order => 0;

        public EvidenceProductionStage Stage => EvidenceProductionStage.BeforeVerification;

        public ValueTask<IReadOnlyList<ProofEvidence>> ProduceAsync(
            EvidenceProductionContext context,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("producer failed");
    }

    private sealed class StaticImpactProvider : IChangeImpactProvider
    {
        public Task<ChangeImpact> AnalyzeAsync(ChangeRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ChangeImpact(
                request.BaseRevision,
                request.HeadRevision,
                request.Spans,
                [],
                [],
                [],
                [],
                "complete",
                false,
                SourceDigest: request.SourceDigest));
    }
}
