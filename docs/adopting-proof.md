# Proof 도입

Proofmark를 기존 저장소에 추가하는 방법을 설명한다. CodeMap은 변경의 영향 범위를 찾고, Distill은 빌드·테스트·분석 결과를 모으며, Proof는 그 결과를 바탕으로 결정적인 인증서를 만든다.

## 준비

- 에이전트와 개발 머신에 [.NET SDK 10.0](https://dotnet.microsoft.com/download)을 설치한다. Distill은 MSBuild binlog를 읽는다. .NET 11 미리보기 SDK가 만든 binlog는 현재 리더보다 새 버전이므로 빌드 검사가 실패할 수 있다. 미리보기 SDK가 함께 설치돼 있다면 `global.json`으로 SDK 10을 고정한다.
- `.gitignore`에 `.codemap/`, `.proof/`, `.distill/`, `bin/`, `obj/`를 추가한다. 인덱스나 실행 산출물이 추적되지 않은 상태로 남아 있으면 인증서에 `SOURCE_FRESHNESS_DRIFT`가 기록될 수 있다.

## 설치

기본 설치 경로는 [nuget.org](https://www.nuget.org)다. .NET SDK 10이 필요하며 런타임만 설치한 환경에서는 사용할 수 없다.

```bash
dotnet tool install --global proof --version 0.4.0
dotnet tool install --global proof-mcp --version 0.4.0
dotnet tool install --global distill --version 0.4.0
dotnet tool install --global distill-mcp --version 0.4.0
dotnet tool install --global Proofmark.CodeMap --version 0.4.0
```

`proof`, `proof-mcp`, `distill`, `distill-mcp`, `Proofmark.CodeMap`은 모두 `0.4.0`을 사용한다. 재현 가능한 설치를 위해 `--version`을 고정한다. `proof verify`만 사용할 때는 `proof`만 설치하면 된다. CodeMap과 Distill 엔진은 `proof` 패키지에 포함되어 있다. 에이전트용 CodeMap MCP는 `Proofmark.CodeMap`의 `codemap mcp` 명령으로 실행한다.

`proof-mcp`를 설치해도 PATH에 `proof` CLI가 추가되지는 않는다. 또한 `Proofmark.CodeMap`의 `codemap` 명령은 nuget.org의 `Codemap.Cli`과 다른 도구다. 두 패키지를 함께 전역 설치하지 않는다.

이 저장소의 소스를 직접 패키징하려면 다음과 같이 한다.

```bash
dotnet pack src/Proof.Cli.Host/Proof.Cli.Host.csproj -c Release -o artifacts/packages
dotnet tool install --global proof --version 0.4.0 --add-source artifacts/packages
```

GitHub Packages에서 설치하는 대체 경로도 있다. 이 방법에는 토큰이 필요하다.

```bash
dotnet nuget add source https://nuget.pkg.github.com/OWNER/index.json \
  --name github --username USER --password "$GITHUB_TOKEN" --store-password-in-clear-text
dotnet tool install --global proof --version 0.4.0
```

## 초기화

```bash
cd your-repository
proof init
proof config validate
```

`proof init`은 `proof.yml.example`을 `proof.yml`로 복사하고, 파일이 없을 때만 `distill.yml`을 만든다. 저장소 루트에 `.slnx` 또는 `.sln`이 있으면 예제의 `YourSolution.slnx`를 실제 파일 이름으로 바꾼다. 이미 있는 파일은 덮어쓰지 않는다. 마지막으로 `analysis.solution`, `policy.testMappingProjects`, `distill.yml`의 검사 대상을 현재 솔루션에 맞춘다.

## MCP

Proof MCP는 기본적으로 읽기 전용이다. 기본 도구는 `proof_plan`, `proof_obligations`, `proof_explain`, `proof_map_suggest`, `proof_summary`, `proof_config_validate`, `proof_certificate_verify`다.

에이전트에서는 보통 `proof-mcp`, `distill-mcp`, `codemap mcp`(`Proofmark.CodeMap`) 세 서버를 실행한다. 각 서버는 `--root`로 워크스페이스를 고정하며, 그 범위를 벗어난 경로를 거부한다. 검사를 실행하는 `proof_verify`는 `--allow-exec` 또는 `PROOF_MCP_ALLOW_EXEC=1`일 때만 등록된다.

Cursor 또는 VS Code(`.cursor/mcp.json` 또는 `.vscode/mcp.json`) 예시. `WORKSPACE`는 저장소 루트 절대 경로다.

```json
{
  "mcpServers": {
    "proof": {
      "command": "dnx",
      "args": ["proof-mcp@0.4.0", "--yes", "--root", "WORKSPACE"]
    },
    "distill": {
      "command": "dnx",
      "args": ["distill-mcp@0.4.0", "--yes", "--root", "WORKSPACE"]
    },
    "codemap": {
      "command": "dnx",
      "args": ["Proofmark.CodeMap@0.4.0", "--yes", "mcp", "--root", "WORKSPACE"]
    }
  }
}
```

검사 실행이 필요할 때만 `proof` 항목의 `args`에 `"--allow-exec"`를 추가한다. 셸에서 직접 실행할 때는 다음과 같다.

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

`.proof/surface/`와 `.proof/base-index/`는 로컬 캐시이므로 커밋하지 않는다.

인증서는 저장소, ref, 워크플로, 커밋 정보를 기록한다. 소비자 저장소에서는 이 정보를 자신의 CI 신원과 맞춰 검증한다.

```bash
proof certificate verify .proof/certificates/latest.json \
  --require-signed \
  --repository OWNER/REPO \
  --allow-ref refs/heads/main \
  --allow-workflow OWNER/REPO/.github/workflows/ci.yml@refs/heads/main \
  --expected-commit-sha "$GITHUB_SHA"
```

서명이 유효하더라도 클레임이 예상과 다르면 신뢰하지 않는다.

예외는 `.proof/exceptions/*.json`에 HMAC으로 서명한다 (`proof exception sign`, 사람 명령). `proof exception evaluate --gate`가 0이어도 판정은 바뀌지 않는다. `.proof/`는 gitignore되므로 `git add -f`하거나 `--output-dir`을 쓴다. CI 평가에는 `PROOF_ATTESTATION_HMAC_KEY`가 필요하다.

## CI

```yaml
- uses: actions/setup-dotnet@v4
  with:
    dotnet-version: '10.0.x'
    source-url: https://nuget.pkg.github.com/OWNER/index.json
  env:
    NUGET_AUTH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
- run: dotnet tool install --global proof --version 0.4.0
- uses: OWNER/proof/.github/actions/proof-verify@v0.4.0
  with:
    proof-command: proof
```

`GITHUB_TOKEN`에는 `packages: read` 권한이 필요하다. `proof-command`를 생략하면 저장소 레이아웃에 맞는 `dotnet run --project src/Proof.Cli.Host/Proof.Cli.Host.csproj`를 사용한다. 서명된 인증서와 `require-signed: true`를 사용하려면 `PROOF_ATTESTATION_HMAC_KEY`도 설정한다.

처음에는 `publicApiCompatibility`와 `staticAnalysis`부터 켜는 편이 좋다. `appContract`와 `architecture`의 기본값은 `off`다. P005는 명시적인 `policy.testMaps` 또는 실제 실행에서 수집한 런타임 커버리지로만 닫을 수 있다.

## 서명 키 운영

- `PROOF_ATTESTATION_HMAC_KEY`는 환경 변수로만 읽는다. 증명서, 로그, `proof.yml`에는 기록하지 않는다. 증명서에는 서명자 신원인 `env:PROOF_ATTESTATION_HMAC_KEY`와 HMAC 값만 남는다. 같은 키를 증명서, 수동 검토, 정책 예외에 사용하지만 페이로드 도메인이 서로 달라 재사용되지는 않는다.
- 키를 교체하면 그 이후 실행부터 새 키로 서명한다. 이전 인증서는 이전 키를 환경 변수로 설정한 뒤 `proof certificate verify`로 검증한다. 하나의 인증서에 키를 두 개 기록하거나 키 ID(`kid`)를 기록하지는 않는다. 이전 키로 서명한 검토와 예외는 새 키로 검증되지 않으므로 다시 서명해야 한다.
- 포크 PR에서는 저장소 시크릿을 사용할 수 없다. `proof-verify` 액션은 `require-signed: true`여도 키가 없으면 `--require-signed`를 넘기지 않고, 요약에 `attestation: unsigned (fork or missing secret)`을 남긴다. 포크 PR의 인증서는 서명되지 않은 봉투이므로 신뢰의 근거로 사용하지 않는다. 머지 후 기본 브랜치에서 생성한 서명 인증서를 기준으로 삼는다.
- 신원 검증 예시다. `OWNER/REPO` 등 자리표시자는 소비자 저장소 값으로 바꾼다.

```bash
proof certificate verify .proof/certificates/latest.json \
  --require-signed \
  --trusted-issuer env:PROOF_ATTESTATION_HMAC_KEY \
  --repository OWNER/REPO \
  --allow-ref refs/heads/main \
  --allow-workflow OWNER/REPO/.github/workflows/ci.yml@refs/heads/main \
  --expected-commit-sha "$COMMIT_SHA"
```

신원 관련 플래그를 하나라도 지정하면 `--require-signed` 없이도 서명되지 않은 인증서를 거부한다.

`toolchain`에는 실행한 `proof.dll`, CodeMap 엔진, Distill 엔진 어셈블리의 SHA-256이 경로를 알 때만 들어간다(단일 파일 게시에서는 비어 있다). `--toolchain-binary <path>`를 주면 그 파일의 해시가 기록된 값 중 하나와 같아야 한다. 다르면 exit 1, 파일이 없으면 exit 2다. 플래그가 없으면 비교하지 않으므로 도구를 올린 뒤에도 옛 증명서는 통과한다.

예외는 판정을 바꾸지 않는다. `proof exception evaluate --gate`는 열린 필수 의무가 모두 유효한 서명 예외로 덮이고 차단 제약이 없을 때만 머지 차단(exit 1)을 해제한다. 액션에서는 `allow-exceptions: true`일 때만 사용한다. exit 2와 4는 예외로 해제할 수 없다.

## 종료 코드와 승격

| exit | 뜻 | CI |
|------|----|----|
| 0 | `PROVEN` 또는 `NO_CHANGE` | 통과 |
| 1 | `NOT_READY`, 또는 `failOnUncertainCodes`에 있는 코드가 만든 `UNCERTAIN` | 머지 차단 |
| 3 | 그 밖의 `UNCERTAIN` | 통과, 경고 |
| 2 / 4 | 설정 또는 인프라 실패 | 실패 |

첫 실행에서 `UNCERTAIN`이 나와도 제품 장애라는 뜻은 아니다. 증거가 부족하다는 뜻일 수 있다. 차단 제약이 있더라도 해당 코드가 `failOnUncertainCodes`에 없으면 exit 3이다. 예를 들어 `uncertainty.impactTruncated: blocking`이 만든 `IMPACT_POTENTIALLY_TRUNCATED`는 기본 목록에 없으므로 exit 3이다.

필수 P005는 다음 순서로 단계적으로 적용한다.

1. `policy.testMappingProjects`에 현재 작업 중인 프로젝트만 넣는다. `policy.ci.failOnUncertainCodes`에서는 `REQUIRED_EVIDENCE_MISSING`만 제외한다. 열린 P005는 인증서와 `proof obligations`에 남지만 CI는 exit 3으로 끝난다.
2. 해당 프로젝트의 변경 심볼이 `testMaps` 또는 런타임 커버리지로 모두 닫히면 `REQUIRED_EVIDENCE_MISSING`을 다시 추가한다. 그때부터 열린 P005는 exit 1이 된다.

`proof.yml.example`은 2단계 상태다. `architecture: required`와 `appContract: required`는 고객 기본값으로 켜지 않는다. `architecture`는 `proof plan`에서 cycle/layer 위반이 0인 뒤에 따로 켠다.

런타임 커버리지는 `testMaps` 없이도 P005를 닫을 수 있다. 필수 P005가 있으면 플래너가 테스트 검사에 `--collect:"XPlat Code Coverage"`를 추가한다. Cobertura에서 `hits > 0`인 메서드가 의무 심볼과 일치하면 `PROVEN`으로 판단한다. Cobertura에는 실행한 테스트 이름이 없으므로 `proof map from-coverage --write`에는 `--test`가 필요하다.

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

`project`에는 `.codemap/scip-providers.json`의 이름과 같은 `scip:{name}`을 쓰고, 해당 디렉터리가 작업 공간에 있어야 한다. 이 검사는 그 프로젝트의 테스트 의무만 덮는다. 보고서와 `coverage`는 이번 실행에서 생성된 파일만 사용한다. 파일이 없거나 이전 실행의 파일이면 검사는 `UNCERTAIN`(`JUNIT_REPORT_MISSING`, `JUNIT_REPORT_STALE`)이 되고 해당 P005는 미해결로 남는다. Windows에서 `npm`처럼 `.cmd` 스크립트로 실행되는 명령은 `cmd /c npm test`로 적는다.

## 예산과 베이스 인덱스

- `analysis.impact.maxResults`의 기본값은 500이다. CodeMap 영향 결과가 한도에서 잘리면(`HasMore`) `IMPACT_POTENTIALLY_TRUNCATED`가 생기고 판정은 `UNCERTAIN`이 된다. 잘림을 숨기기 위해 한도만 높이지 말고, 변경을 나누거나 해당 상태를 그대로 확인한다.
- `analysis.indexBaseRevision: true`는 베이스 커밋의 git worktree에서 CodeMap 인덱스를 새로 만든다. 삭제·이름 변경된 심볼의 호출자를 베이스 그래프에서 찾을 때만 필요하다. 인덱스 한 번 분량의 시간이 더 든다. `proof.yml.example`은 `false`, 이 저장소의 `proof.yml`은 `true`다. 끄면 추적하지 못한 삭제는 `CHANGE_DELETION_ANALYSIS_UNAVAILABLE`로 남는다. CI에서 켜려면 `fetch-depth: 0`이 필요하다.
- CodeMap 내부 상한(부분집합 스캔 400, outgoing 페이지 200)은 [performance-notes.md](../engines/codemap/docs/performance-notes.md)에 있다. Proof의 500과 별개이며 운영 저장소 벤치마크 전에는 바꾸지 않는다.

의무와 종료 코드는 [architecture.md](architecture.md).
