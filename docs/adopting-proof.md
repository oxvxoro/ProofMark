# Proof 도입

CodeMap이 영향을 보고, Distill이 빌드·테스트·분석 산출물을 모으며, Proof가 결정적 인증서를 만든다.

## 준비

- 에이전트와 개발 머신에 [.NET SDK 10.0](https://dotnet.microsoft.com/download). Distill은 MSBuild binlog를 읽는다. .NET 11 미리보기 SDK가 쓴 binlog는 리더보다 새로워 빌드 검사가 실패하므로, 미리보기가 같이 있으면 `global.json`으로 SDK 10을 고정한다.
- `.gitignore`에 `.codemap/`, `.proof/`, `.distill/`, `bin/`, `obj/`. 추적되지 않은 인덱스나 실행 산출물이 있으면 인증서가 `SOURCE_FRESHNESS_DRIFT`를 낸다.

## 설치

기본 경로는 [nuget.org](https://www.nuget.org)다. .NET SDK 10이 필요하다. 런타임만으로는 부족하다.

```bash
dotnet tool install --global proof --version 0.3.0
dotnet tool install --global proof-mcp --version 0.3.0
dotnet tool install --global distill --version 0.3.0
dotnet tool install --global distill-mcp --version 0.3.0
dotnet tool install --global Proofmark.CodeMap --version 0.3.0
```

`proof`, `proof-mcp`, `distill`, `distill-mcp`, `Proofmark.CodeMap`은 버전 `0.3.0`을 공유한다. `--version`을 고정한다. `proof verify`에는 `proof`만 있으면 된다. CodeMap과 Distill 엔진은 `proof` 안에 들어 있다. 에이전트용 CodeMap MCP는 `Proofmark.CodeMap`의 `codemap mcp`다. `proof-mcp`를 설치해도 PATH에 `proof` CLI가 생기지 않는다. `codemap` 명령은 nuget.org의 `Codemap.Cli`과 다른 게시자(`Proofmark.CodeMap`)다. 두 `codemap` 도구를 함께 전역 설치하지 않는다.

이 저장소에서 로컬 팩:

```bash
dotnet pack src/Proof.Cli.Host/Proof.Cli.Host.csproj -c Release -o artifacts/packages
dotnet tool install --global proof --version 0.3.0 --add-source artifacts/packages
```

GitHub Packages(토큰 필요, 대체 경로):

```bash
dotnet nuget add source https://nuget.pkg.github.com/OWNER/index.json \
  --name github --username USER --password "$GITHUB_TOKEN" --store-password-in-clear-text
dotnet tool install --global proof --version 0.3.0
```

## 초기화

```bash
cd your-repository
proof init
proof config validate
```

`proof init`은 `proof.yml.example`을 `proof.yml`로 복사하고, 없을 때만 `distill.yml`을 만든다. 루트에 `.slnx` 또는 `.sln`이 있으면 `YourSolution.slnx`를 그 이름으로 바꾼다. 있는 파일은 덮어쓰지 않는다. `analysis.solution`, `policy.testMappingProjects`, `distill.yml`의 검사를 솔루션에 맞춘다.

## MCP

기본은 읽기 전용이다. `proof_plan`, `proof_obligations`, `proof_explain`, `proof_map_suggest`, `proof_summary`, `proof_config_validate`, `proof_certificate_verify`.

에이전트는 보통 MCP 서버 세 개를 띄운다. `proof-mcp`, `distill-mcp`, `codemap mcp`( `Proofmark.CodeMap` ). 각 서버는 `--root`로 워크스페이스를 고정하고, 고정 밖 경로는 거부한다. `proof_verify`는 `--allow-exec`(또는 `PROOF_MCP_ALLOW_EXEC=1`)일 때만 등록된다.

Cursor 또는 VS Code(`.cursor/mcp.json` 또는 `.vscode/mcp.json`) 예시. `WORKSPACE`는 저장소 루트 절대 경로다.

```json
{
  "mcpServers": {
    "proof": {
      "command": "dnx",
      "args": ["proof-mcp@0.3.0", "--yes", "--root", "WORKSPACE"]
    },
    "distill": {
      "command": "dnx",
      "args": ["distill-mcp@0.3.0", "--yes", "--root", "WORKSPACE"]
    },
    "codemap": {
      "command": "dnx",
      "args": ["Proofmark.CodeMap@0.3.0", "--yes", "mcp", "--root", "WORKSPACE"]
    }
  }
}
```

실행이 필요할 때만 `proof` 항목에 `"--allow-exec"`를 `args`에 추가한다. 셸에서 직접:

```bash
proof-mcp --root .
distill-mcp --root .
codemap mcp --root .
proof-mcp --allow-exec --root .
```

`proof-mcp`는 `proof` CLI 호스트(`proof.dll`)를 패키지 옆에 둔다. `distill-mcp`는 `distill_report`, `distill_diagnostics`, `distill_raw`, `distill_doctor`다.

## 로컬과 신원

```bash
codemap index .
proof plan
proof verify
proof explain
proof summary --format markdown
```

`.proof/surface/`와 `.proof/base-index/`는 로컬 캐시다. 커밋하지 않는다.

인증서는 저장소, ref, 워크플로, 커밋을 기록한다. 소비자 저장소는 자기 신원에 묶는다.

```bash
proof certificate verify .proof/certificates/latest.json \
  --require-signed \
  --repository OWNER/REPO \
  --allow-ref refs/heads/main \
  --allow-workflow OWNER/REPO/.github/workflows/ci.yml@refs/heads/main \
  --expected-commit-sha "$GITHUB_SHA"
```

서명이 맞아도 클레임이 다르면 신뢰하지 않는다.

예외는 `.proof/exceptions/*.json`에 HMAC으로 서명한다 (`proof exception sign`, 사람 명령). `proof exception evaluate --gate`가 0이어도 판정은 바뀌지 않는다. `.proof/`는 gitignore되므로 `git add -f`하거나 `--output-dir`을 쓴다. CI 평가에는 `PROOF_ATTESTATION_HMAC_KEY`가 필요하다.

## CI

```yaml
- uses: actions/setup-dotnet@v4
  with:
    dotnet-version: '10.0.x'
    source-url: https://nuget.pkg.github.com/OWNER/index.json
  env:
    NUGET_AUTH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
- run: dotnet tool install --global proof --version 0.3.0
- uses: OWNER/proof/.github/actions/proof-verify@v0.3.0
  with:
    proof-command: proof
```

`GITHUB_TOKEN`에 `packages: read`가 필요하다. `proof-command`가 비어 있으면 dogfood 레이아웃인 `dotnet run --project src/Proof.Cli.Host/Proof.Cli.Host.csproj`를 쓴다. 서명 인증서와 `require-signed: true`가 필요하면 `PROOF_ATTESTATION_HMAC_KEY`를 설정한다.

자주 켜는 정책은 `publicApiCompatibility`와 `staticAnalysis`다. `appContract`와 `architecture`의 기본은 `off`다. P005를 정직하게 닫는 것은 명시적 `policy.testMaps`와 런타임 커버리지만이다.

## 서명 키 운영

- `PROOF_ATTESTATION_HMAC_KEY`는 환경 변수로만 읽는다. 증명서, 로그, `proof.yml`에 쓰지 않는다. 증명서에는 서명자 신원 `env:PROOF_ATTESTATION_HMAC_KEY`와 HMAC 값만 남는다. 같은 키가 증명, 수동 리뷰, 정책 예외를 서명하고, 페이로드 도메인이 달라 서로 재사용되지 않는다.
- 회전: 새 키를 시크릿에 넣으면 이후 실행만 새 키로 서명된다. 이전 증명서는 이전 키를 환경에 두고 `proof certificate verify`로 검증한다. 한 증명서에 키 두 개를 넣지 않고 키 ID(`kid`)도 없다. 이전 키로 서명한 리뷰와 예외는 새 키에서 검증되지 않으므로 다시 서명한다(사람 명령).
- 포크 PR은 저장소 시크릿을 받지 못한다. `proof-verify` 액션은 `require-signed: true`여도 키가 비어 있으면 `--require-signed`를 넘기지 않고, 요약에 `attestation: unsigned (fork or missing secret)`을 적는다. 포크 PR의 증명서는 서명 없는 봉투이며 신뢰 근거가 아니다. 머지 후 기본 브랜치 실행의 서명 증명서를 기준으로 삼는다.
- 신원 검증 예시(자리표시자를 소비자 저장소 값으로 바꾼다):

```bash
proof certificate verify .proof/certificates/latest.json \
  --require-signed \
  --trusted-issuer env:PROOF_ATTESTATION_HMAC_KEY \
  --repository OWNER/REPO \
  --allow-ref refs/heads/main \
  --allow-workflow OWNER/REPO/.github/workflows/ci.yml@refs/heads/main \
  --expected-commit-sha "$COMMIT_SHA"
```

신원 플래그를 하나라도 주면 서명 없는 증명서는 `--require-signed` 없이도 거부된다.

`toolchain`에는 실행한 `proof.dll`, CodeMap 엔진, Distill 엔진 어셈블리의 SHA-256이 경로를 알 때만 들어간다(단일 파일 게시에서는 비어 있다). `--toolchain-binary <path>`를 주면 그 파일의 해시가 기록된 값 중 하나와 같아야 한다. 다르면 exit 1, 파일이 없으면 exit 2다. 플래그가 없으면 비교하지 않으므로 도구를 올린 뒤에도 옛 증명서는 통과한다.

예외는 판정을 바꾸지 않는다. `proof exception evaluate --gate`는 열린 required 의무가 모두 유효한 서명 예외로 덮이고 blocking 제약이 0일 때만 머지 차단(exit 1)을 푼다. 액션에서는 `allow-exceptions: true`일 때만 쓴다. exit 2와 4는 예외로 풀리지 않는다.

## 종료 코드와 승격

| exit | 뜻 | CI |
|------|----|----|
| 0 | `PROVEN` 또는 `NO_CHANGE` | 통과 |
| 1 | `NOT_READY`, 또는 `failOnUncertainCodes`에 있는 코드가 만든 `UNCERTAIN` | 머지 차단 |
| 3 | 그 밖의 `UNCERTAIN` | 통과, 경고 |
| 2 / 4 | 설정 또는 인프라 실패 | 실패 |

첫 실행의 `UNCERTAIN`은 제품 장애가 아니다. 증거가 부족하다는 판정이다. blocking 제약이 있어도 그 코드가 `failOnUncertainCodes`에 없으면 exit 3이다. 예를 들어 `uncertainty.impactTruncated: blocking`이 만든 `IMPACT_POTENTIALLY_TRUNCATED`는 기본 목록에 없어서 exit 3이다.

required P005는 단계적으로 올린다.

1. `policy.testMappingProjects`에 지금 고치는 프로젝트만 둔다. `policy.ci.failOnUncertainCodes`에서 `REQUIRED_EVIDENCE_MISSING`만 뺀다. 열린 P005는 인증서와 `proof obligations`에 남고 CI는 exit 3이다.
2. 그 프로젝트의 변경 심볼이 `testMaps` 또는 런타임 커버리지로 닫히면 `REQUIRED_EVIDENCE_MISSING`을 다시 넣는다. 이제 열린 P005는 exit 1이다.

`proof.yml.example`은 2단계 상태다. `architecture: required`와 `appContract: required`는 고객 기본값으로 켜지 않는다. `architecture`는 `proof plan`에서 cycle/layer 위반이 0인 뒤에 따로 켠다.

런타임 커버리지는 `testMaps` 없이 P005를 닫는다. 필수 P005가 있으면 플래너가 테스트 검사에 `--collect:"XPlat Code Coverage"`를 붙이고, Cobertura에서 `hits > 0`인 메서드가 의무 심볼과 맞으면 `PROVEN`이다. Cobertura는 실행한 테스트 이름을 주지 않는다. 그래서 `proof map from-coverage --write`에는 `--test`가 필요하다.

비-.NET 테스트는 `distill.yml`의 `kind: process`로 실행한다. 종료 코드만으로는 어떤 의무도 닫히지 않는다. SCIP로 인덱싱한 프로젝트의 테스트 결과를 쓰려면 JUnit XML과 프로젝트를 적는다.

```yaml
checks:
  web-tests:
    kind: process
    command: npm test
    source: junit
    artifact: web/reports/junit.xml
    project: scip:web
    coverage: web/coverage/cobertura.xml
```

`project`는 `.codemap/scip-providers.json`의 이름과 같은 `scip:{name}`이고 작업 공간에 그 디렉터리가 있어야 한다. 이 검사는 그 프로젝트의 테스트 의무만 덮는다. 보고서와 `coverage`는 이번 실행 중에 쓰인 파일만 읽는다. 없거나 이전 파일이면 검사는 `UNCERTAIN`(`JUNIT_REPORT_MISSING`, `JUNIT_REPORT_STALE`)이고 해당 P005는 미해결로 남는다. Windows에서 `npm` 같은 `.cmd` 스크립트는 `cmd /c npm test`로 적는다.

## 예산과 베이스 인덱스

- `analysis.impact.maxResults`의 기본은 500이다. CodeMap 영향 결과가 한도에서 잘리면(`HasMore`) `IMPACT_POTENTIALLY_TRUNCATED`가 생기고 verdict는 `UNCERTAIN`이 된다. 한도를 올려 잘림을 숨기지 않는다. 변경을 나누거나 잘림을 그대로 둔다.
- `analysis.indexBaseRevision: true`는 베이스 커밋의 git worktree에서 CodeMap 인덱스를 새로 만든다. 삭제·이름 변경된 심볼의 호출자를 베이스 그래프에서 찾을 때만 필요하다. 인덱스 한 번 분량의 시간이 더 든다. `proof.yml.example`은 `false`, 이 저장소의 `proof.yml`은 `true`다. 끄면 추적하지 못한 삭제는 `CHANGE_DELETION_ANALYSIS_UNAVAILABLE`로 남는다. CI에서 켜려면 `fetch-depth: 0`이 필요하다.
- CodeMap 내부 상한(부분집합 스캔 400, outgoing 페이지 200)은 [performance-notes.md](../engines/codemap/docs/performance-notes.md)에 있다. Proof의 500과 별개이며 운영 저장소 벤치마크 전에는 바꾸지 않는다.

의무와 종료 코드는 [architecture.md](architecture.md).
