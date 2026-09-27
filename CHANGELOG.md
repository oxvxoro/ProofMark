# 변경 기록

Proofmark 도구 패키지(`proof`, `proof-mcp`, `distill`, `distill-mcp`, `Proofmark.CodeMap`)의 눈에 띄는 변경을 적는다. 도구는 `Version.props`의 버전 하나를 공유한다.

## [Unreleased]

## [0.3.0] - 릴리스 태그 시점

### 추가

- `proof`, `proof-mcp`, `distill`, `distill-mcp`, `Proofmark.CodeMap` .NET 도구 패키지. `distill.yml.example`과 도입 문서를 포함한다.
- `proof-mcp`와 `distill-mcp`가 MCP 서버 패키지(`PackageType` `McpServer`)로 팩되며 `.mcp/server.json`을 nupkg 루트에 넣는다. `proof-mcp`는 `dotnet exec`로 CLI 도구를 실행하므로 `proof` CLI 호스트(`proof.dll`, deps, runtimeconfig)를 옆에 둔다.
- `release.yml`이 태그 `v*` 빌드를 GitHub Packages에 게시하고, nuget.org Trusted Publishing(`NuGet/login@v1`, `release.yml` 정책)으로 nuget.org에도 게시한다.
- CI `pack` 게이트: 도구 다섯 개를 설치하고, `fixtures/proof/simple-service`의 독립 복사가 판정 `PROVEN`과 종료 코드 0에 도달해야 한다. `proof-mcp`, `distill-mcp`, `codemap mcp`를 stdio로 스모크한다(`tools/list`, 고정 `--root` 밖 거부). `release.yml`은 게시 전에 같은 스모크를 실행한다.
- CI `pack-windows`: ubuntu `pack`이 만든 nupkg로 Windows에서 도구 설치와 MCP 스모크를 실행한다.
- `proof-windows` CI 잡: windows-latest에서 기본 프로파일 `proof verify`.
- `consumer-feed` 워크플로(`workflow_dispatch`): GitHub Packages에서 도구를 설치하고 같은 소비자 골든을 실행한다.
- `proof history`: `.proof/certificates/*.summary.json`의 인증서 분석(판정 분포, 규칙별 미해결 의무와 P005 매핑 부채, 차단 이유 코드, `--rule`, `--format json`). 분석일 뿐이다. 과거 인증서는 현재 변경의 증거가 아니다.
- `proof exception`: 서명된 정책 예외(`list`, `verify`, `sign`, `evaluate`). 거버넌스일 뿐이다. 예외는 의무 상태나 인증서 판정을 바꾸지 않는다(`Exception != Proven`).
- 증명 신원 클레임(ref, workflow ref, workflow sha, commit sha, run attempt)과 `proof certificate verify` 옵션 `--repository`, `--allow-ref`, `--allow-workflow`, `--expected-commit-sha`.
- `fixtures/proof/coverage-service` 골든과 `coverage-golden` 워크플로(`workflow_dispatch`, 주간): testMaps 없이 런타임 커버리지(`RUNTIME_COVERAGE`, `BIND_TEST_MAPPING`)만으로 P005가 `PROVEN`에 도달한다. `consumer_golden.py`는 픽스처의 `golden.json`으로 바꿀 파일과 기대 규칙, 증거 종류를 받는다.
- `docs/adopting-proof.md`에 nuget.org 설치, MCP `dnx` 설정, 종료 코드와 `failOnUncertainCodes` 승격 순서, 영향 예산과 `indexBaseRevision` 비용 절을 적었다.
- `P010AspNetGoldenTests`: CodeMap AspNetFixture를 실제로 인덱싱해 `profile: app`, `appContract: required`에서 라우트 P010이 처리기 Cobertura 적중으로만 `PROVEN`이 되는지 본다. 저장소 `proof.yml`은 P010을 계속 끈다.
- 인증서 `toolchain`에 선택 속성 `proofBinarySha256`, `codeMapBinarySha256`, `distillBinarySha256`이 생겼다. 어셈블리 경로를 알 때만 채우고, null이면 직렬화하지 않아 schema 3과 기존 statement 다이제스트가 그대로다. `proof certificate verify --toolchain-binary <path>`(반복 가능)는 줄 때만 비교한다. 불일치는 exit 1, 파일이 없으면 exit 2다.
- `docs/adopting-proof.md`의 서명 키 운영 절(환경 변수 전용 키, 회전, 포크 PR, `--trusted-issuer` 예시, 예외 게이트 한계).
- `docs/ci/azure-pipelines-proof.yml`: GitHub 액션과 같은 종료 코드 규칙의 Azure Pipelines 예시. 결과는 로그와 아티팩트다.
- `SECURITY.md`: 비공개 보고 경로, 키 취급, 보안 스캔과 SBOM이 증명 증거가 아니라는 범위.
- CodeMap 호출자 조회가 `Calls`와 함께 `scip:` 프로젝트의 semantic `References`를 읽는다. 저장 엣지는 그대로이고 C# 타입 이름 `References`와 heuristic SCIP 참조는 호출자가 아니다.
- CodeMap 인덱서가 `.codemap/scip-providers.json`에 선언되고 파일이 있는 SCIP 산출물을 해시가 바뀌면 `UpdateAsync`, `index`, `index --force`에서 다시 임포트한다. 선언이 없거나 파일이 없는 스테일 산출물은 여전히 예외다.
- Distill `kind: process`의 `source: junit`(`artifact`, `project`)과 `coverage` 키. JUnit XML은 테스트 케이스 증거가 되고, `project: scip:{name}` 검사는 그 프로젝트의 테스트 의무만 덮는다. 명시한 Cobertura는 이번 실행에서 쓰였을 때만 커버리지 생산자로 간다.
- CodeMap·Distill MCP가 `--root`로 워크스페이스를 고정하고, 고정 밖 `root` 인자를 거부한다.

### 변경

- `Version.props`의 `VersionPrefix` 하나(0.3.0)가 모든 Proofmark 도구 패키지 버전의 단일 출처다.
- 도구 패키지는 저장소의 `artifacts/packages/`에 모인다. `codemap` 패키지는 `engines/codemap/artifacts/packages/`에서 옮겨 왔다.
- Proof MCP 기본은 읽기 전용이다. `proof_verify`는 `--allow-exec`(또는 `PROOF_MCP_ALLOW_EXEC=1`)일 때만 등록되고, 서버는 워크스페이스 루트를 고정한다(`--root`, 기본은 작업 디렉터리).
- `proof-verify` 복합 액션에 `allow-exceptions` 입력이 생겼다(기본 꺼짐). 켜면 NOT_READY 또는 머지 차단 결과가, `proof exception evaluate --gate`가 의무 주도 차단을 유효한 서명 예외로 모두 덮었다고 확인할 때만 CI 머지 게이트를 지운다. 열린 필수 의무가 덮이고 차단 제약이 없어야 한다. 인증서 판정은 바꾸지 않는다.
- 증명 검증은 `AttestationProviderRegistry`를 통한다. 이후 키 없는 비대칭 제공자를 위한 경계다. 내장 `none` / `hmac-sha256`은 그대로이고, 알 수 없는 제공자는 신뢰하지 않는다.
- 이름이 어긋나 있던 `UnsignedAttestationSigner` / `UnsignedAttestationVerifier`를 `HmacAttestationSigner` / `HmacAttestationVerifier`로 바꿨다. 동작은 같다.
- CodeMap 도구 패키지 ID는 `Proofmark.CodeMap`이다. 도구 명령은 `codemap`이다. nuget.org의 `Codemap.Cli`과 `Proofmark.CodeMap`은 다른 게시자다.

### 수정

- `.gitignore`가 일시 파일 `.proof-e2e.*`를 무시한다.
- Distill `kind: process`가 명령을 `dotnet`으로 실행하던 문제를 고쳤다. 첫 토큰을 실행 파일로 실행하고 종료 코드만 증거로 남긴다. Proof는 process 명령에서 대상을 파싱하지 않고, 재작성하지 않으며, 그 증거를 어떤 의무에도 direct로 붙이지 않는다.
- CodeMap 솔루션 재조정이 선언되지 않은 `scip:` 프로젝트를 지우던 문제를 고쳤다.
- P010 라우트 의무를 런타임 커버리지로 닫을 수 없던 문제를 고쳤다. 주체가 `route://` 노드라 Cobertura 메서드와 맞지 않았다. 깊이 1의 semantic `RoutesTo`로 바뀐 처리기에 곧바로 이어진 라우트만 그 처리기의 적중으로 닫힌다.
- CI `pack`과 `release.yml`이 `Distill.Cli.Host` 도구 대신 `Distill.Cli` 라이브러리를 팩해 `distill` 도구 패키지가 나오지 않던 문제를 고쳤다.
- `distill doctor`가 MCP stdio 호스트에서 멈추던 문제를 고쳤다. 프로브 프로세스가 JSON-RPC stdin을 물려받고 stderr를 비우지 않았다. 프로브는 닫힌 stdin을 받고 두 출력 파이프를 동시에 비운다.
- 메서드와 생성자 주제의 `displayName`에 멤버 이름이 두 번 들어가던 문제를 고쳤다. CodeMap C# 시그니처가 이미 이름을 포함한다. 테스트 케이스 증거는 테스트 프로젝트를 경로(`Tests/Tests.csproj`)로 들고, CodeMap 주제는 프로젝트 이름(`Tests`)을 쓴다. 둘 중 하나면 `BIND_TEST_CASE` / `BIND_CALLER_TEST`가 정확히 맞지 않아, 소비자 저장소에서 영향받은 테스트가 `Unresolved`로 남았다.
- 변경 집합이 클 때 Windows에서 변경 캡처가 교착되던 문제를 고쳤다. `git cat-file --batch` 출력을 비우기 전에 입력을 모두 쓰면, 작은 익명 파이프 버퍼가 찬 뒤 막힌다. Proof는 stdout/stderr를 동시에 비운다. 기준 인덱스 git 호출(`git worktree` 등)도 두 파이프를 비운다.
