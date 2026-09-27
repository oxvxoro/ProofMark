# Proof 아키텍처

CodeMap은 무엇이 바뀌고 무엇에 영향을 주는지를 본다. Distill은 빌드, 테스트, 분석 산출물을 모은다. Proof는 그 증거가 변경에 충분한지를 판정한다. 로드맵은 [roadmap.md](roadmap.md).

```text
Proof.Cli
   ↓
Proof.Engine
  ↙            ↘
Proof.Adapters.CodeMap   Proof.Adapters.Distill
        ↓                         ↓
   CodeMap.Engine              Distill.Core
```

CodeMap과 Distill은 서로와 Proof를 참조하지 않는다. `Proof.Adapters.Git`만 `Distill.Git`을 참조한다. 엔진은 git을 직접 띄우지 않고 `IAttestationContextResolver`를 받는다.

## 불변 조건

1. 핵심 판정은 결정적이다.
2. LLM은 증명 생성과 판정에 참여하지 않는다.
3. CodeMap은 Distill에 의존하지 않는다.
4. Distill은 CodeMap에 의존하지 않는다.
5. Proof는 명시적 어댑터로만 둘을 쓴다.
6. 구조화된 증거가 텍스트 파싱보다 우선한다.
7. 통과한 검사만으로 충분성이 증명되지 않는다.
8. 알 수 없거나 불완전한 커버리지는 숨기지 않는다.
9. 휴리스틱 관계는 조용히 의미 사실이 되지 않는다.
10. 증거 출처는 변경 인증서까지 남는다.
11. CodeMap과 Distill의 독립 워크플로는 그대로 테스트할 수 있다.
12. 마이그레이션과 의미 리팩터링은 서로 다른 변경이다.

## 판정

| 판정 | 의미 |
| --- | --- |
| `PROVEN` | 필수 의무가 모두 주체 범위의 인정 가능한 증거로 증명됨 |
| `NOT_READY` | 필수 의무에 권위 있는 실패 증거가 있음 |
| `UNCERTAIN` | 필수 실패는 없으나 미해결 의무나 차단 제약이 남음 |
| `NO_CHANGE` | 잡힌 변경 집합이 비어 있음 |
| `INFRA_ERROR` | 검증 인프라가 실패함 |

Distill `PASS`는 검사가 쓸 수 있는 증거를 냈다는 뜻이다. Proof `PROVEN`은 그 증거가 변경에 충분하다는 뜻이다.

종료 코드: `0` 준비 또는 변경 없음, `1` `NOT_READY` 또는 머지 차단 `UNCERTAIN`, `3` 권고 `UNCERTAIN`, `2`/`4` 설정 또는 인프라.

## 증명

`PROOF_ATTESTATION_HMAC_KEY`가 있으면 `HmacAttestationSigner`가 문장 다이제스트에 `hmac-sha256` 봉투를 만든다. 키가 없으면 `none`이다. 키는 봉투, 인증서, 서명 페이로드에 들어가지 않는다. HMAC 입력은 `proofmark:attestation:v2|<statementDigest>`다. v1 봉투는 이관 중에만 검증되고, 새 서명은 v2다. 증명 서명은 수동 검토나 예외 서명으로 재사용할 수 없다. 비교는 hex 디코드 뒤 `CryptographicOperations.FixedTimeEquals`다.

`TrustPolicy.TrustedIssuers`가 비어 있지 않으면 서명이 맞아도 `SignerIdentity`가 목록에 있어야 `Trusted`다. `Repository`, `AllowedRefs`, `AllowedWorkflows`, `ExpectedCommitSha`가 설정된 항은 해당 클레임이 있어야 한다. 서명 유효와 신뢰는 분리된다. `branch`만 있는 레거시 인증서는 `--allow-ref`를 만족한다. 설정되지 않은 필드는 강제하지 않는다.

클레임은 ref, workflow ref, workflow sha, commit sha, run attempt다. 사람 행위자는 클레임이 아니다. 구현은 `GitAttestationContextResolver`가 CI 환경을 먼저 보고, 이어서 `origin` URL과 `rev-parse`를 정규화한다.

키 없는 제공자(OIDC, Sigstore)는 등록하지 않는다. 등록되지 않은 제공자는 신뢰하지 않는다. 도구 바이너리 다이제스트는 아직 기록하지 않는다.

## 의무

**P001A 공개 API.** 비교 기준은 머지베이스(`proof.base`)다. 헤드 인덱스가 아니다. 스냅샷은 `.proof/surface/{commitSha}.json`이며 커밋하지 않는다. 없으면 `.proof/base-index/{sha}`를 보거나, `analysis.indexBaseRevision: true`이고 변경된 공개 심볼이 있으면 워크트리에서 한 번 만든다. 기준이 없으면 `Inconclusive`다. 헤드 지문만 맞아 `Pass`가 되지 않는다.

**영향 절단.** 변경 루트마다 CodeMap `ImpactPaged`의 `HasMore`를 본다. 합집합이 `analysis.impact.maxResults`에 닿았다는 이유만으로 절단이 아니다. 한 페이지라도 `HasMore`이면 `IMPACT_POTENTIALLY_TRUNCATED`가 될 수 있다.

**테스트 분류 (P002/P004).** `IsTestProject`가 true이거나 `Microsoft.NET.Test.Sdk`를 참조하면 그 프로젝트의 심볼은 테스트다. 명시적 비테스트 프로젝트는 `*Tests` 이름으로 테스트가 되지 않는다. 프로젝트 파일이 말하지 않으면 MSBuild `IsTestProject`를 묻고, 그것도 없으면 그 프로젝트만 경로 휴리스틱을 쓴다.

**삭제 추적.** 삭제되거나 이름이 바뀐 옛 경로는 헤드 인덱스에 없다. 갱신 전 인덱스, 또는 `analysis.indexBaseRevision: true`일 때 기준 리비전 워크트리 인덱스에서 호출자를 추적한다. 해소된 경로만 `DeletionPathsResolved`로 차단 제약 `CHANGE_DELETION_ANALYSIS_UNAVAILABLE`에서 빠진다. 나머지는 `UNCERTAIN`이다.

**P002.** 테스트 호출자만 호출자 계약 의무를 만든다. 프로덕션 호출자는 P002를 만들지 않는다. `BIND_CALLER_COMPILE`은 `supporting`이며 그 주장을 증명하지 않는다 (불변 조건 7).

**P005.** 호출자 관계는 테스트가 심볼을 덮는다는 증거가 아니다. 생산자는 증거를 내지 않는다. 닫는 원천은 `proof.yml`의 명시적 맵과, 실제 실행의 런타임 커버리지만이다. 일치는 완전 일치, 시그니처를 자른 일치, 점 접미사다. 짧은 메서드 이름만으로는 맞지 않는다. 맵된 변경 심볼은 P004를 내고 그 심볼의 P005를 억제한다. 한 맵 항목은 다른 심볼의 P005를 닫지 않는다.

`policy.testMappingProjects`는 필수 P005를 CodeMap 프로젝트 이름(`.csproj` 파일 이름)의 완전 일치로 제한한다. 대소문자는 무시한다. 글롭과 경로 조각은 없다. 생략하거나 빈 목록이면 전역이다. 범위 밖 심볼은 `Required: false`인 P005로 남는다. 판정, `runtime-coverage` 선택, CI 차단에는 쓰이지 않는다.

`proof map add --accept`는 사람이 한다. 파일 전체를 다시 직렬화하지 않고, 파싱에 실패하면 원래 바이트를 복구하고 종료 코드 2다. `--accept`가 없으면 아무것도 쓰지 않는다. `proof map from-coverage`의 `--write`는 명시적 `--test`가 필요하다. Cobertura는 실행한 테스트 이름을 모른다. `proof map suggest`는 조언만이다.

런타임 커버리지는 `dotnet test`에 `--collect:"XPlat Code Coverage"`를 붙이고 `coverage/**/coverage.cobertura.xml`을 읽는다. `hits > 0`인 메서드만 같은 신원 함수로 P005에 맞춘다. 다이제스트가 없거나 오래되면 `BIND_SOURCE_DIGEST`로 거절한다. 산출물이 없으면 증거가 없다.

**경로.** `ignore`는 스냅샷에서 빠진다. 델타가 모두 무시되면 `NO_CHANGE`다. `manual-review`는 변경 집합에 남고 필수 P009를 만든다. 바이너리, 서브모듈, 추적되지 않은 삭제도 P009다. P009는 `.proof/reviews/*.json`의 서명 검토만으로 닫힌다. v2 페이로드는 `proofmark:review:v2|<digest>|<subjectId>|<reviewer>`다. v1은 `policy.manualReview.acceptedSchemas`로 이관 중에 받는다. `proof review sign`은 사람이 한다. 검토 파일은 `.proof/`에 있으며 커밋하지 않는다.

**P008.** `policy.staticAnalysis: required`이면 영향받은 프로젝트마다 의무가 하나다. 저장소 전체 분석은 `supporting`만이다. 진단이 비어 있고 프로젝트 주제가 있으면 그 실행이 P008을 직접 증명한다. SARIF의 프로젝트 출처는 `result.properties.project`, 없으면 run 속성 또는 `automationDetails.id`다. dogfood는 quick와 full 모두에 `analyzers`가 있어 기본 verify로 P008이 닫힌다.

**P010.** `analysis.impact.profile`이 `code`면 참조 엣지만, `app`이면 HTTP/UI 흐름 엣지도 따라간다. 의무는 `policy.appContract: required`일 때만 생긴다. 의미적이고 신뢰도를 통과한 앱 엣지의 비테스트 심볼만 대상이다. 휴리스틱은 P010이 되지 않는다. 테스트 FQN은 P010을 닫지 않는다. 주제가 맞는 `RuntimeCoverage`만 `direct`다. 빌드와 정적 분석은 `supporting`이다. dogfood는 `profile: code`이고 `appContract`는 꺼져 있다.

**P011.** `off | advisory | required`. 기본은 `off`이며 검사를 하지 않는다. 켜지면 헤드 그래프에서 휴리스틱 엣지를 뺀 뒤 `ArchitectureChecker`를 실행한다. Razor/XAML의 구문 엣지는 유지한다. `required`에서 cycle과 layer만 필수다. fan-in, fan-out, orphan은 필수가 아니다. `advisory`는 판정을 바꾸지 않는다. `required`인데 `.codemap/architecture.json`이 없으면 필수 `architecture-rules` 의무가 `Unresolved`가 되고, 동시에 기본 한도로 찾은 cycle/layer도 보고한다. 조용한 `Pass`는 없다. P011은 `EvidenceKind.Architecture`만 닫는다. SARIF와 빌드는 보조도 되지 않는다. dogfood는 `advisory`다.

## 부가 산출물

인증서 옆 `.summary.json`과 `.proof/runs/{runId}/metrics.json`은 진단이다. `ComputeStatementDigest`에 들어가지 않는다.

`verification.cache: true` 또는 `PROOF_CACHE=1|true`는 `.proof/cache/{key}.json`을 쓴다. 키는 소스 다이제스트, 프로파일, 재작성된 명령의 SHA-256이다. 더러운 트리와 깨끗한 트리는 공유하지 않는다. 적중하면 커버리지 경로를 비운다. 저장된 `SourceDigest`가 계획과 다르면 항목 전체를 버린다.

`verification.importManifest`는 계획된 검사가 모두 있고 `sourceDigest`, `kind`, `commandDigest`, 산출물 SHA-256이 맞을 때만 신뢰한다. 하나라도 어긋나면 전부를 다시 실행한다. 가져오기는 증거 캐시를 우회한다.

## MCP, 이력, 예외

`proof-mcp`의 기본 도구는 `proof_plan`, `proof_obligations`, `proof_explain`, `proof_map_suggest`, `proof_summary`, `proof_config_validate`, `proof_certificate_verify`다. CLI와 같은 코드 경로다. `proof_verify`는 `--allow-exec` 또는 `PROOF_MCP_ALLOW_EXEC=1`일 때만 등록된다. `map add`와 `review sign`은 없다. `--root` 밖의 `root` 인자는 거부된다.

`proof history`는 과거 요약의 분포만 보여 준다. 과거 `PROVEN`은 현재 변경의 증거가 아니다.

`proof exception`은 `.proof/exceptions/*.json`의 서명된 예외다. 예외는 의무 상태와 판정을 바꾸지 않는다. `evaluate --gate`는 열린 필수 의무가 있고 모두 덮였으며 차단 제약이 없을 때만 0이다. 인증서 문장은 수정하지 않는다. `.proof/`는 gitignore되므로 CI에 넘기려면 `git add -f`하거나 `--output-dir`로 추적되는 경로에 쓴다. 키가 없으면 `unverifiable`이며 유효로 보지 않는다.

## CI

`policy.ci.failOnUncertainCodes`에 있는 코드가 평가 이유, 차단 제약, 또는 미해결 필수 의무의 이유이면 `UNCERTAIN`의 종료 코드가 1이 된다. `Required: false`는 빼 둔다. 목록에 없으면 종료 코드 3이다. dogfood는 `CHANGE_DELETION_ANALYSIS_UNAVAILABLE`, `CHANGE_CAPTURE_UNTRACKED_FAILED`, `SOURCE_FRESHNESS_DRIFT`, `REQUIRED_EVIDENCE_MISSING`을 올린다. 플래너 산문은 이유 코드가 아니다. P005만 `REQUIRED_EVIDENCE_MISSING`으로 떨어진다.

필수 검사는 [repository-settings.md](repository-settings.md)와 같다. 포크 PR은 키가 없으면 서명 없는 봉투를 허용한다. `proof`와 `proof-full`은 `continue-on-error`가 아니다.
