using System.Diagnostics;
using Proof.Core;

namespace Proof.Engine;

public sealed class ProofOrchestrator
{
    private readonly ISourceSnapshotCollector? _snapshotCollector;
    private readonly IChangeImpactProvider _impactProvider;
    private readonly IProofPlanner _planner;
    private readonly IVerificationRunner _verificationRunner;
    private readonly IProofEvaluator _evaluator;
    private readonly IChangeCertificateBuilder _certificateBuilder;
    private readonly IVerificationPlanner _verificationPlanner;
    private readonly IEvidenceBinder _evidenceBinder;
    private readonly IReadOnlyList<IEvidenceProducer> _evidenceProducers;
    private readonly IAttestationSigner? _attestationSigner;
    private readonly IAttestationContextResolver? _attestationContextResolver;
    private readonly IProofMetricsSink? _metricsSink;

    public ProofOrchestrator(
        IChangeImpactProvider impactProvider,
        IProofPlanner planner,
        IVerificationRunner verificationRunner,
        IProofEvaluator evaluator,
        IChangeCertificateBuilder certificateBuilder,
        IVerificationPlanner? verificationPlanner = null,
        ISourceSnapshotCollector? snapshotCollector = null,
        IEvidenceBinder? evidenceBinder = null,
        IApiCompatibilityEvidenceProducer? apiCompatibilityProducer = null,
        IAttestationSigner? attestationSigner = null,
        ITestMappingEvidenceProducer? testMappingEvidenceProducer = null,
        IAttestationContextResolver? attestationContextResolver = null,
        IRuntimeCoverageEvidenceProducer? runtimeCoverageEvidenceProducer = null,
        IManualReviewEvidenceProducer? manualReviewEvidenceProducer = null,
        IArchitectureEvidenceProducer? architectureEvidenceProducer = null,
        IReadOnlyList<IEvidenceProducer>? evidenceProducers = null,
        IProofMetricsSink? metricsSink = null)
    {
        _impactProvider = impactProvider;
        _planner = planner;
        _verificationRunner = verificationRunner;
        _evaluator = evaluator;
        _certificateBuilder = certificateBuilder;
        _verificationPlanner = verificationPlanner ?? new VerificationPlanner();
        _snapshotCollector = snapshotCollector;
        _evidenceBinder = evidenceBinder ?? new EvidenceBinder();
        _evidenceProducers = BuildEvidenceProducers(
            apiCompatibilityProducer,
            testMappingEvidenceProducer,
            runtimeCoverageEvidenceProducer,
            manualReviewEvidenceProducer,
            architectureEvidenceProducer,
            evidenceProducers);
        _attestationSigner = attestationSigner;
        _attestationContextResolver = attestationContextResolver;
        _metricsSink = metricsSink;
    }

    public Task<ChangeCertificate> VerifyAsync(
        SourceSnapshot snapshot,
        string distillProfile,
        CancellationToken cancellationToken,
        ProofPolicy? policy = null,
        IReadOnlyList<EvidenceCapability>? capabilities = null,
        CertificateMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var request = SourceSnapshotCollector.ToChangeRequest(snapshot);
        return VerifyAsync(request, distillProfile, cancellationToken, policy, capabilities, metadata);
    }

    public async Task<ChangeCertificate> VerifyFromWorkspaceAsync(
        string workspaceRoot,
        string baseRevision,
        string headRevision,
        string distillProfile,
        CancellationToken cancellationToken,
        ProofPolicy? policy = null,
        IReadOnlyList<EvidenceCapability>? capabilities = null,
        CertificateMetadata? metadata = null)
    {
        if (_snapshotCollector is null)
        {
            throw new InvalidOperationException("VerifyFromWorkspaceAsync requires an ISourceSnapshotCollector.");
        }

        var snapshotWatch = Stopwatch.StartNew();
        var snapshot = await _snapshotCollector.CollectAsync(
            workspaceRoot,
            baseRevision,
            headRevision,
            cancellationToken).ConfigureAwait(false);
        snapshotWatch.Stop();
        snapshot = ApplyPathPolicy(snapshot, policy?.PathRules);
        metadata = (metadata ?? new CertificateMetadata()) with { IsDirty = snapshot.IsDirty };
        var request = SourceSnapshotCollector.ToChangeRequest(snapshot);
        var certificate = await VerifyCoreAsync(
            request,
            distillProfile,
            cancellationToken,
            policy,
            capabilities,
            metadata,
            attest: false,
            snapshotMs: snapshotWatch.Elapsed.TotalMilliseconds).ConfigureAwait(false);
        var fresh = await _snapshotCollector.CollectAsync(
            workspaceRoot,
            baseRevision,
            headRevision,
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(fresh.SourceDigest, snapshot.SourceDigest, StringComparison.Ordinal))
        {
            certificate = ApplyFreshnessDrift(certificate);
        }

        return await AttachAttestationAsync(
            certificate,
            request.WorkspaceRoot,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ChangeCertificate> VerifyAsync(
        ChangeRequest request,
        string distillProfile,
        CancellationToken cancellationToken,
        ProofPolicy? policy = null,
        IReadOnlyList<EvidenceCapability>? capabilities = null,
        CertificateMetadata? metadata = null)
        => await VerifyCoreAsync(
            request,
            distillProfile,
            cancellationToken,
            policy,
            capabilities,
            metadata,
            attest: true).ConfigureAwait(false);

    private async Task<ChangeCertificate> VerifyCoreAsync(
        ChangeRequest request,
        string distillProfile,
        CancellationToken cancellationToken,
        ProofPolicy? policy,
        IReadOnlyList<EvidenceCapability>? capabilities,
        CertificateMetadata? metadata,
        bool attest,
        double? snapshotMs = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var timings = _metricsSink is null ? null : new PhaseTimings();
        var prepared = await PrepareAsync(
            request,
            distillProfile,
            policy,
            capabilities,
            cancellationToken,
            timings).ConfigureAwait(false);
        var impact = prepared.Analysis.Impact;
        var plan = prepared.ProofPlan;
        // null 카탈로그는 능력 계획을 요청하지 않았다는 뜻이다. 러너와
        // 바인더는 그래도 계획 객체가 필요하다. 빈 계획은 검사를 받아들이지 않고
        // 능력 누락 제약을 구체화하지 않는다.
        var verificationPlan = prepared.VerificationPlan
            ?? new VerificationPlan([], [], distillProfile) with { SourceDirty = request.IsDirty };

        // 러너는 설계상 링크가 빈 증거 집합을 반환한다.
        // 오케스트레이터가 아래 _evidenceBinder.Bind로 바인딩을 소유한다. 검증 전
        // 생산자는 러너와 동시에 돌지만, 출력은 나중에 (Order, Name) 순서로
        // 합쳐진다. 그래서 작업 완료 순서가 인증서를
        // 결코 바꿀 수 없다.
        var verificationWatch = Stopwatch.StartNew();
        var orderedProducers = _evidenceProducers
            .OrderBy(item => item.Order)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();
        var beforeProducers = orderedProducers
            .Where(item => item.Stage == EvidenceProductionStage.BeforeVerification)
            .ToArray();
        var beforeContext = new EvidenceProductionContext(
            request,
            impact,
            plan,
            verificationPlan,
            VerificationRun: null,
            prepared.Analysis.ApiCompatibilityFacts);
        var beforeTasks = beforeProducers
            .Select(producer => ProduceAsync(producer, beforeContext, cancellationToken))
            .ToArray();

        var runnerTask = _verificationRunner.VerifyAsync(
            plan,
            verificationPlan,
            request.WorkspaceRoot,
            cancellationToken);
        await Task.WhenAll(runnerTask, Task.WhenAll(beforeTasks)).ConfigureAwait(false);

        var executed = await runnerTask.ConfigureAwait(false);
        var production = new EvidenceProductionContext(
            request,
            impact,
            plan,
            verificationPlan,
            executed,
            prepared.Analysis.ApiCompatibilityFacts);

        var produced = new List<(IEvidenceProducer Producer, IReadOnlyList<ProofEvidence> Evidence)>();
        for (var index = 0; index < beforeProducers.Length; index++)
        {
            produced.Add((beforeProducers[index], await beforeTasks[index].ConfigureAwait(false)));
        }

        foreach (var producer in orderedProducers.Where(item => item.Stage == EvidenceProductionStage.AfterVerification))
        {
            produced.Add((producer, await producer.ProduceAsync(production, cancellationToken).ConfigureAwait(false)));
        }

        var evidence = executed.Evidence.Evidence.ToList();
        foreach (var item in produced
                     .OrderBy(entry => entry.Producer.Order)
                     .ThenBy(entry => entry.Producer.Name, StringComparer.Ordinal))
        {
            evidence.AddRange(item.Evidence);
        }

        verificationWatch.Stop();

        var bindingWatch = Stopwatch.StartNew();
        var bound = _evidenceBinder.Bind(plan, evidence, verificationPlan);
        bindingWatch.Stop();

        var evaluationWatch = Stopwatch.StartNew();
        var evaluation = _evaluator.Evaluate(plan, bound);
        evaluationWatch.Stop();

        var certificateWatch = Stopwatch.StartNew();
        var certificate = _certificateBuilder.Build(
            impact,
            plan,
            bound,
            evaluation,
            (metadata ?? new CertificateMetadata()) with
            {
                VerificationPlan = verificationPlan,
                WorkspaceRoot = request.WorkspaceRoot
            });
        certificateWatch.Stop();

        _metricsSink?.Record(new VerificationMetrics(
            snapshotMs,
            timings?.ImpactMs ?? 0,
            timings?.PlanningMs ?? 0,
            verificationWatch.Elapsed.TotalMilliseconds,
            bindingWatch.Elapsed.TotalMilliseconds,
            evaluationWatch.Elapsed.TotalMilliseconds,
            certificateWatch.Elapsed.TotalMilliseconds,
            new VerificationMetricCounts(
                impact.ChangedSymbols.Count,
                impact.ImpactedSymbols.Count,
                impact.Callers is { Count: > 0 } callers ? callers.Count : impact.CallerSubjectIds.Count,
                plan.Obligations.Count,
                evidence.Count,
                bound.Links.Count)));

        if (attest)
        {
            certificate = await AttachAttestationAsync(certificate, request.WorkspaceRoot, cancellationToken)
                .ConfigureAwait(false);
        }

        return certificate;
    }

    private async Task<ChangeCertificate> AttachAttestationAsync(
        ChangeCertificate certificate,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        if (_attestationSigner is null
            || string.IsNullOrWhiteSpace(certificate.StatementDigest))
        {
            return certificate;
        }

        // 증명은 statement 페이로드의 일부가 아니므로, 서명한 뒤에
        // statement/인증서 다이제스트를 다시 계산하지 않는다.
        // 출처 해석은 포트다. 해석기가 없으면 컨텍스트는
        // 실행 id만 담는다(repository/branch 클레임은 없다).
        var context = _attestationContextResolver is not null
            ? await _attestationContextResolver
                .ResolveAsync(workspaceRoot, certificate.RunId ?? string.Empty, cancellationToken)
                .ConfigureAwait(false)
            : new AttestationContext(certificate.RunId ?? string.Empty);
        var envelope = await _attestationSigner.SignAsync(
            certificate.StatementDigest,
            context,
            cancellationToken).ConfigureAwait(false);
        return certificate with { Attestation = envelope };
    }

    public async Task<(SourceSnapshot Snapshot, ChangeImpact Impact, ProofPlan Plan, VerificationPlan? VerificationPlan)> PlanAsync(
        SourceSnapshot snapshot,
        string distillProfile,
        CancellationToken cancellationToken,
        ProofPolicy? policy = null,
        IReadOnlyList<EvidenceCapability>? capabilities = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot = ApplyPathPolicy(snapshot, policy?.PathRules);
        var request = SourceSnapshotCollector.ToChangeRequest(snapshot);
        var (impact, plan, verificationPlan) = await PlanAsync(
            request,
            distillProfile,
            cancellationToken,
            policy,
            capabilities).ConfigureAwait(false);
        return (snapshot, impact, plan, verificationPlan);
    }

    public async Task<(ChangeImpact Impact, ProofPlan Plan, VerificationPlan? VerificationPlan)> PlanAsync(
        ChangeRequest request,
        string distillProfile,
        CancellationToken cancellationToken,
        ProofPolicy? policy = null,
        IReadOnlyList<EvidenceCapability>? capabilities = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var prepared = await PrepareAsync(
            request,
            distillProfile,
            policy,
            capabilities,
            cancellationToken).ConfigureAwait(false);
        return (prepared.Analysis.Impact, prepared.ProofPlan, prepared.VerificationPlan);
    }

    private static IReadOnlyList<IEvidenceProducer> BuildEvidenceProducers(
        IApiCompatibilityEvidenceProducer? apiCompatibility,
        ITestMappingEvidenceProducer? testMapping,
        IRuntimeCoverageEvidenceProducer? runtimeCoverage,
        IManualReviewEvidenceProducer? manualReview,
        IArchitectureEvidenceProducer? architecture,
        IReadOnlyList<IEvidenceProducer>? additional)
    {
        var producers = new List<IEvidenceProducer>(5 + (additional?.Count ?? 0));
        if (apiCompatibility is not null)
        {
            producers.Add(new ApiCompatibilityEvidenceAdapter(apiCompatibility));
        }

        if (testMapping is not null)
        {
            producers.Add(new TestMappingEvidenceAdapter(testMapping));
        }

        if (runtimeCoverage is not null)
        {
            producers.Add(new RuntimeCoverageEvidenceAdapter(runtimeCoverage));
        }

        if (manualReview is not null)
        {
            producers.Add(new ManualReviewEvidenceAdapter(manualReview));
        }

        if (architecture is not null)
        {
            producers.Add(new ArchitectureEvidenceAdapter(architecture));
        }

        if (additional is { Count: > 0 })
        {
            producers.AddRange(additional);
        }

        return producers;
    }

    private static async Task<IReadOnlyList<ProofEvidence>> ProduceAsync(
        IEvidenceProducer producer,
        EvidenceProductionContext context,
        CancellationToken cancellationToken)
        => await producer.ProduceAsync(context, cancellationToken).ConfigureAwait(false);

    private async Task<AnalyzedChange> AnalyzeChangeAsync(
        ChangeRequest request,
        CancellationToken cancellationToken)
    {
        ChangeImpact raw;
        IReadOnlyList<ApiCompatibilityFact> facts;
        if (_impactProvider is IChangeAnalysisProvider analysis)
        {
            var result = await analysis.AnalyzeChangeAsync(request, cancellationToken).ConfigureAwait(false);
            raw = result.Impact;
            facts = result.ApiCompatibilityFacts;
        }
        else
        {
            raw = await _impactProvider.AnalyzeAsync(request, cancellationToken).ConfigureAwait(false);
            facts = [];
        }

        return new AnalyzedChange(request, ProofPreparation.NormalizeImpact(request, raw), facts);
    }

    // capabilities == null. 호출자가 카탈로그를 주지 않았다. 그래서 능력
    // 계획은 건너뛰고 능력 누락 제약은 더하지 않는다.
    // capabilities != null, 빈 카탈로그 포함. 카탈로그가
    // 주어졌으므로, 덮이지 않은 필수 의무를 구체화한다.
    private async Task<PreparedProof> PrepareAsync(
        ChangeRequest request,
        string distillProfile,
        ProofPolicy? policy,
        IReadOnlyList<EvidenceCapability>? capabilities,
        CancellationToken cancellationToken,
        PhaseTimings? timings = null)
    {
        var analysisWatch = Stopwatch.StartNew();
        var analysis = await AnalyzeChangeAsync(request, cancellationToken).ConfigureAwait(false);
        analysisWatch.Stop();
        if (timings is not null)
        {
            timings.ImpactMs = analysisWatch.Elapsed.TotalMilliseconds;
        }

        var planWatch = Stopwatch.StartNew();
        var plan = _planner.Plan(analysis.Impact, policy) with
        {
            SourceDigest = request.SourceDigest,
            ChangeSetIsEmpty = request.ChangeSetIsEmpty
        };
        planWatch.Stop();
        if (timings is not null)
        {
            timings.PlanningMs = planWatch.Elapsed.TotalMilliseconds;
        }

        if (capabilities is null)
        {
            return new PreparedProof(analysis, plan, null);
        }

        var verificationPlan = _verificationPlanner.Plan(plan, capabilities, distillProfile) with
        {
            SourceDirty = request.IsDirty
        };
        plan = MaterializeUncovered(plan, verificationPlan);
        return new PreparedProof(analysis, plan, verificationPlan);
    }

    // 경로 정책 필터(Wave B step 4). 영향 분석 전에 스냅샷에서
    // 무시된 파일을 뺀다. 스팬은 FileDeltas에서 나오므로,
    // 델타를 지우면 그 스팬도 지워진다. 모든 델타가 무시되면
    // 변경 집합은 비고 판정은 정직하게 NO_CHANGE일 수 있다.
    internal static SourceSnapshot ApplyPathPolicy(SourceSnapshot snapshot, IReadOnlyList<PathRule>? rules)
    {
        if (rules is not { Count: > 0 } || snapshot.Files.Count == 0)
        {
            return snapshot;
        }

        var kept = snapshot.Files
            .Where(delta => !PathPolicy.IsIgnored(delta.NewPath ?? delta.OldPath ?? string.Empty, rules))
            .ToArray();
        if (kept.Length == snapshot.Files.Count)
        {
            return snapshot;
        }

        return snapshot with
        {
            Files = kept,
            ChangeSetIsEmpty = kept.Length == 0 || snapshot.ChangeSetIsEmpty
        };
    }

    private static ProofPlan MaterializeUncovered(ProofPlan plan, VerificationPlan verificationPlan)
    {
        if (verificationPlan.Uncovered.Count == 0)
        {
            return plan;
        }

        var constraints = (plan.Constraints ?? Array.Empty<AnalysisConstraint>()).ToList();
        foreach (var uncovered in verificationPlan.Uncovered)
        {
            constraints.Add(new AnalysisConstraint(
                DeterministicProofPlanner.StableId("C", ProofReasonCodes.RequiredEvidenceCapabilityMissing, uncovered.ObligationId),
                ProofReasonCodes.RequiredEvidenceCapabilityMissing,
                "blocking",
                $"Required obligation '{uncovered.ObligationId}' is uncovered in profile '{verificationPlan.Profile}'.",
                uncovered.ObligationId));
        }

        return plan with { Constraints = constraints };
    }

    private ChangeCertificate ApplyFreshnessDrift(ChangeCertificate certificate)
    {
        var constraints = (certificate.Plan.Constraints ?? Array.Empty<AnalysisConstraint>()).ToList();
        constraints.Add(new AnalysisConstraint(
            DeterministicProofPlanner.StableId("C", ProofReasonCodes.SourceFreshnessDrift),
            ProofReasonCodes.SourceFreshnessDrift,
            "blocking",
            "Source digest changed during verification; result is not bound to a stable snapshot."));
        var plan = certificate.Plan with { Constraints = constraints };
        var evaluation = certificate.Evaluation with
        {
            Verdict = ProofVerdict.Uncertain,
            ReasonCode = ProofReasonCodes.SourceFreshnessDrift
        };
        var unsigned = certificate with
        {
            Verdict = ProofVerdict.Uncertain,
            Plan = plan,
            Evaluation = evaluation,
            Constraints = constraints,
            CertificateDigest = null,
            StatementDigest = null,
            Attestation = null
        };
        var statement = CertificateCanonicalHasher.ComputeStatementDigest(unsigned);
        unsigned = unsigned with { StatementDigest = statement };
        return unsigned with { CertificateDigest = CertificateCanonicalHasher.ComputeDigest(unsigned) };
    }

    // 실행당 가변 단계 시간. 메트릭 싱크가 설정된 때만 할당한다.
    private sealed class PhaseTimings
    {
        public double ImpactMs { get; set; }

        public double PlanningMs { get; set; }
    }
}
