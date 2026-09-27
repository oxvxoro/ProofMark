using Proof.Adapters.CodeMap;
using Proof.Adapters.Distill;
using Proof.Adapters.Git;
using Proof.Core;
using Proof.Engine;

namespace Proof.Cli;

internal sealed record ProofRuntimeContext(
    string WorkspaceRoot,
    ProofConfig Config,
    string DistillConfigPath,
    ProofOrchestrator Orchestrator);

/// <summary>
/// 구성 루트. <see cref="Create"/>는 런타임을 연결할 뿐이다. 분석
/// 옵션과 증거 생산자는 집중된 도우미가 만들므로, 이 메서드는
/// 팩터리 본문이 아니라 읽기 쉬운 의존성 그래프로 남는다.
/// </summary>
internal static class ProofRuntimeFactory
{
    public static ProofRuntimeContext Create(
        string workspaceRoot,
        ProofConfig? config = null,
        IProofMetricsSink? metricsSink = null)
    {
        config ??= ProofConfig.Load(workspaceRoot);
        var compiled = ProofConfigCompiler.Compile(config);
        var distillConfigPath = DistillVerificationRunner.ResolveConfigPath(workspaceRoot, compiled.Verification.DistillConfig);
        var analysisOptions = CreateAnalysisOptions(workspaceRoot, config, compiled);
        var distillRunner = new DistillVerificationRunner(
            distillConfigPath,
            compiled.Verification.Cache,
            compiled.Verification.ImportManifest);
        var producers = CreateEvidenceProducers(compiled);
        var orchestrator = new ProofOrchestrator(
            new CodeMapChangeImpactProvider(analysisOptions),
            new DeterministicProofPlanner(),
            distillRunner,
            new DeterministicProofEvaluator(),
            new ChangeCertificateBuilder(),
            snapshotCollector: new GitSourceSnapshotCollector(),
            apiCompatibilityProducer: producers.ApiCompatibility,
            attestationSigner: new HmacAttestationSigner(),
            testMappingEvidenceProducer: producers.TestMapping,
            attestationContextResolver: new GitAttestationContextResolver(),
            runtimeCoverageEvidenceProducer: producers.RuntimeCoverage,
            manualReviewEvidenceProducer: producers.ManualReview,
            architectureEvidenceProducer: producers.Architecture,
            metricsSink: metricsSink);
        return new ProofRuntimeContext(workspaceRoot, config, distillConfigPath, orchestrator);
    }

    internal static CodeMapAnalysisOptions CreateAnalysisOptions(
        string workspaceRoot,
        ProofConfig config,
        CompiledProofConfig compiled)
    {
        var solutionPath = AnalysisTargetResolver.Resolve(workspaceRoot, config.Analysis.Solution);
        return new CodeMapAnalysisOptions(
            workspaceRoot,
            solutionPath,
            compiled.Impact,
            config.Analysis.IndexBaseRevision,
            RunArchitectureCheck: compiled.Policy.Architecture != ArchitecturePolicyMode.Off);
    }

    internal static ProofEvidenceProducers CreateEvidenceProducers(CompiledProofConfig compiled)
        => new(
            new ApiCompatibilityEvidenceProducer(),
            new YamlTestMappingEvidenceProducer(compiled.Policy.TestMaps),
            new RuntimeCoverageEvidenceProducer(),
            new ManualReviewEvidenceProducer(compiled.Policy.ManualReviewAcceptedSchemas),
            new ArchitectureEvidenceProducer());
}

internal sealed record ProofEvidenceProducers(
    IApiCompatibilityEvidenceProducer ApiCompatibility,
    ITestMappingEvidenceProducer TestMapping,
    IRuntimeCoverageEvidenceProducer RuntimeCoverage,
    IManualReviewEvidenceProducer ManualReview,
    IArchitectureEvidenceProducer Architecture);
