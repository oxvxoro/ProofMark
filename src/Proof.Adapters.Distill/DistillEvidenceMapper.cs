using Distill.Core.Diagnostics;
using Distill.Core.Planning;
using Distill.Core.Runs;
using Proof.Core;

namespace Proof.Adapters.Distill;

internal static class DistillEvidenceMapper
{
    internal static List<ProofEvidence> Map(
        ProofPlan proofPlan,
        IReadOnlyList<PlannedCheck> plannedChecks,
        IReadOnlyList<CheckRunResult> results)
    {
        var plannedById = plannedChecks.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var evidence = new List<ProofEvidence>();
        var evidenceIndex = 0;

        foreach (var result in results)
        {
            evidenceIndex++;
            plannedById.TryGetValue(result.CheckId, out var planned);
            // kind: process는 dotnet 명령이 아니므로 명령 대상이 없다. 종료 코드 증거는
            // 주체 참조가 없어 어떤 의무에도 direct로 붙지 않는다. 테스트 케이스는
            // source: junit 보고서에서 읽은 것만 남긴다.
            var isProcess = string.Equals(result.Kind, "process", StringComparison.OrdinalIgnoreCase);
            var keepCases = !isProcess || string.Equals(result.SourceId, "junit", StringComparison.OrdinalIgnoreCase);
            string? commandTarget = null;
            if (planned is not null && !isProcess)
            {
                try
                {
                    commandTarget = DotnetCommandParser.Parse(planned.Definition.Command).Target;
                }
                catch (ArgumentException)
                {
                    commandTarget = null;
                }
            }

            IReadOnlyList<global::Distill.Core.Evidence.TestCaseEvidence> executedCases =
                keepCases ? result.ExecutedCases ?? [] : [];
            var caseNames = executedCases
                .Select(item => item.FullyQualifiedName ?? item.Name)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .ToArray();

            var scopeSubjects = result.Diagnostics
                .Select(diagnostic => diagnostic.TestName)
                .Concat(result.Diagnostics.Select(diagnostic => diagnostic.Project))
                .Concat(caseNames)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var artifactSha256 = ArtifactHasher.ComputeSha256Hex(result.ArtifactPointer);
            var kind = MapCheckKind(result.Kind);
            var scopeMode = ResolveScopeMode(commandTarget);
            var evidenceStatus = MapStatus(result.Status);
            IReadOnlyList<EvidenceSubjectRef>? scopeSubjectRefs = (kind == EvidenceKind.Build || kind == EvidenceKind.StaticAnalysis)
                                                                    && commandTarget is not null
                                                                    && (commandTarget.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                                                                        || commandTarget.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase))
                ? new[]
                {
                    new EvidenceSubjectRef(
                        SubjectKind.Project,
                        Path.GetFileNameWithoutExtension(commandTarget),
                        Path.GetFileNameWithoutExtension(commandTarget))
                }
                : null;

            if (kind == EvidenceKind.StaticAnalysis)
            {
                // 분석 증거의 주체 범위는 진단이 가리키는 프로젝트다. 오류 진단은
                // 증거를 실패시킨다.
                // 프로젝트 범위 클론(csproj 명령 대상)은 진단의 Project 필드가
                // 비었거나 다른 프로젝트여도 자기 프로젝트 참조를 유지해야 한다.
                // 그렇지 않으면 진단이 있는 클론이 프로젝트 범위를 조용히 잃는다.
                // 저장소 전체 실행은 프로젝트 범위를 결코 주장하지 않는다. 솔루션 전체
                // 분석은 supporting으로 남는다.
                var projectScoped = commandTarget is not null
                                    && (commandTarget.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                                        || commandTarget.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase));
                if (projectScoped)
                {
                    scopeSubjectRefs = ResolveStaticAnalysisProjectRefs(
                        Path.GetFileNameWithoutExtension(commandTarget!),
                        result.Diagnostics);
                }

                if (result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                {
                    evidenceStatus = EvidenceStatus.Fail;
                }
            }

            if (kind == EvidenceKind.ApiCompatibility)
            {
                scopeSubjectRefs = ResolveApiCompatProjectRefs(commandTarget, result.Diagnostics);
                if (result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                {
                    evidenceStatus = EvidenceStatus.Fail;
                }
                else if (scopeSubjectRefs.Count == 0)
                {
                    evidenceStatus = EvidenceStatus.Inconclusive;
                }
            }

            evidence.Add(new ProofEvidence(
                $"E{evidenceIndex}",
                kind,
                result.CheckId,
                evidenceStatus,
                new EvidenceProvenance(
                    "distill",
                    result.ArtifactPointer,
                    proofPlan.SourceDigest,
                    result.CheckId,
                    artifactSha256),
                new EvidenceScope(
                    scopeMode,
                    scopeSubjects.Length == 0 ? null : scopeSubjects,
                    commandTarget,
                    scopeSubjectRefs)));

            foreach (var testCase in executedCases)
            {
                evidenceIndex++;
                var identity = testCase.FullyQualifiedName ?? testCase.Name;
                evidence.Add(new ProofEvidence(
                    $"E{evidenceIndex}",
                    EvidenceKind.TestCase,
                    identity,
                    MapTestOutcome(testCase.Outcome),
                    new EvidenceProvenance(
                        "distill",
                        result.ArtifactPointer,
                        proofPlan.SourceDigest,
                        result.CheckId,
                        artifactSha256),
                    new EvidenceScope(
                        ScopeMode.Exact,
                        [identity],
                        commandTarget,
                        [
                            new EvidenceSubjectRef(
                                SubjectKind.Test,
                                identity,
                                ProjectName(testCase.Project),
                                DisplayName: testCase.Name,
                                FullyQualifiedName: identity)
                        ])));
            }
        }

        return evidence;
    }

    /// <summary>
    /// 정적 분석 증거의 프로젝트 주체 참조. 명령 대상의
    /// 프로젝트는 항상 앞에 오고, 자기 Project 필드를 가진 진단은
    /// 뒤에 붙는다. SARIF 진단이 도착해도 프로젝트 클론의 범위는 유지된다
    /// (#pragma나 생성 코드 억제가 다른 프로젝트를
    /// 가리킬 수 있다).
    /// </summary>
    internal static IReadOnlyList<EvidenceSubjectRef> ResolveApiCompatProjectRefs(
        string? commandTarget,
        IReadOnlyList<DistillDiagnostic> diagnostics)
    {
        var refs = new List<EvidenceSubjectRef>();
        if (commandTarget is not null
            && (commandTarget.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                || commandTarget.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)))
        {
            var project = Path.GetFileNameWithoutExtension(commandTarget);
            refs.Add(new EvidenceSubjectRef(SubjectKind.Project, project, project));
        }

        refs.AddRange(diagnostics
            .Select(diagnostic => diagnostic.Project)
            .Where(project => !string.IsNullOrWhiteSpace(project))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(project => project, StringComparer.Ordinal)
            .Select(project => new EvidenceSubjectRef(SubjectKind.Project, project, project)));

        return refs
            .DistinctBy(reference => reference.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static IReadOnlyList<EvidenceSubjectRef> ResolveStaticAnalysisProjectRefs(
        string commandTargetProject,
        IReadOnlyList<DistillDiagnostic> diagnostics)
    {
        var refs = new List<EvidenceSubjectRef>
        {
            new(SubjectKind.Project, commandTargetProject, commandTargetProject)
        };

        refs.AddRange(diagnostics
            .Select(diagnostic => diagnostic.Project)
            .Where(project => !string.IsNullOrWhiteSpace(project))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(project => project, StringComparer.Ordinal)
            .Select(project => new EvidenceSubjectRef(SubjectKind.Project, project, project)));

        return refs
            .DistinctBy(reference => reference.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// 테스트 호스트는 프로젝트를 명령 대상 경로로 보고한다
    /// (<c>Tests/Tests.csproj</c>). CodeMap 주체는 프로젝트 이름을 담고
    /// (<c>Tests</c>), 동일성 비교는 프로젝트가 어긋나면 거부한다.
    /// </summary>
    internal static string? ProjectName(string? project)
    {
        if (string.IsNullOrWhiteSpace(project))
        {
            return project;
        }

        var normalized = project.Replace('\\', '/');
        return normalized.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(normalized)
            : project;
    }

    internal static ScopeMode ResolveScopeMode(string? commandTarget)
    {
        if (string.IsNullOrWhiteSpace(commandTarget))
        {
            return ScopeMode.RepositoryWide;
        }

        var normalized = commandTarget.Replace('\\', '/');
        return normalized.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
            ? ScopeMode.RepositoryWide
            : normalized.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
                ? ScopeMode.Exact
                : ScopeMode.Contains;
    }

    private static EvidenceKind MapCheckKind(string checkKind)
        => checkKind.ToLowerInvariant() switch
        {
            "build" => EvidenceKind.Build,
            "test" => EvidenceKind.TestRun,
            "analysis" => EvidenceKind.StaticAnalysis,
            "apicompatibility" or "api_compatibility" => EvidenceKind.ApiCompatibility,
            "testmapping" or "test-mapping" => EvidenceKind.TestMapping,
            "runtimecoverage" or "runtime-coverage" or "coverage" => EvidenceKind.RuntimeCoverage,
            "manualreview" or "manual-review" or "process" => EvidenceKind.ManualReview,
            _ => Enum.TryParse<EvidenceKind>(checkKind, true, out var parsed)
                ? parsed
                : EvidenceKind.ManualReview
        };

    private static EvidenceStatus MapStatus(VerificationStatus status)
        => status switch
        {
            VerificationStatus.Pass => EvidenceStatus.Pass,
            VerificationStatus.Fail => EvidenceStatus.Fail,
            VerificationStatus.InfraError => EvidenceStatus.InfraError,
            _ => EvidenceStatus.Inconclusive
        };

    private static EvidenceStatus MapTestOutcome(string outcome)
        => outcome.ToLowerInvariant() switch
        {
            "passed" or "pass" => EvidenceStatus.Pass,
            "failed" or "fail" => EvidenceStatus.Fail,
            "skipped" or "notexecuted" => EvidenceStatus.Skipped,
            _ => EvidenceStatus.Inconclusive
        };
}
