# 에이전트 통합

CodeMap은 C#과 JS/TS/HTML/CSS의 로컬 의미 인덱스다. 모든 조사에 쓰지 않는다. 심볼이나 진입점을 찾고 관계를 한 단계 추적할 때만 쓴다. 범위를 벗어나면 `grep`과 파일 읽기로 넘어간다.

## 최소 절차

1. `codemap index` 또는 수정 뒤 `codemap update`. 인덱싱된 프로젝트는 `.codemap/state.json`.
2. `codemap find <query>`.
3. 관계 명령은 하나. `flow`, `callees`, `callers`, `refs`, `impl`, `impact`.
4. 반환된 파일과 줄을 소스에서 읽는다. 답이 나면 멈춘다.

집중된 질문에서 `map`과 `context`로 시작하지 않는다.

```text
find → 관계 명령 1개 → 소스 읽기 → 종료
```

| 목적 | 명령 |
| --- | --- |
| 심볼 위치 | `find` |
| 앞으로의 호출, HTTP, UI | `flow` 또는 `callees` |
| 누가 호출하는지 | `callers` |
| 변경 영향 | `impact --changed` 또는 `diff` |

한 조사에서는 CLI와 MCP 중 하나만 쓴다. 반복 조사에서 MCP가 이미 붙어 있으면 그 프로세스를 재사용한다.

| CLI | MCP |
| --- | --- |
| `find` | `find_symbol` |
| `flow` | `get_flow` |
| `impact` | `get_impact` |

첫 조사의 `get_flow`는 `includeEvidence=false`다. 고른 엣지만 `explain_relation`으로 증명한다.

## grep으로 넘길 때

- 레거시 WebForms: `.aspx` 인라인 스크립트, `App_Code`, code-behind. `web:<name>`만 있으면 독립 `.js`/`.ts`만 인덱싱된 것이다.
- 네이티브 DLL, 메타데이터를 읽을 수 없는 어셈블리, 소스가 닿지 않은 외부 루트. 관리되는 외부 어셈블리는 소스가 도달한 심볼의 같은 어셈블리 그래프만 best-effort다. 완전성을 보장하지 않는다. 조회는 소스가 우선이다.
- 종료 코드 2 또는 빈 결과가 두 번. 비슷한 이름으로 `find`를 반복하지 않는다.
- JS 문자열 안의 HTTP 경로(`xmlhttp.open`, `fetch("aspx/...")`). 서버 핸들러 `flow` 엣지가 아니다.

제거된 사용법: `codemap .`, `--deps`, `--importers`. 답이 난 뒤 관계 명령을 더 실행하지 않는다.

## 신뢰도

| `resolutionKind` | 의미 |
| --- | --- |
| `semantic` | Roslyn C#과 `RoutesTo` / `Registers` / `ResolvesTo`. 신뢰도 1.0 |
| `syntactic` | CSS, Razor, Blazor, XAML. `Renders` / `HandlesEvent` / `UsesViewModel` 0.90, `BindsTo` 0.85 |
| `heuristic` | 웹 정규식 또는 AST. 0.55–0.70 |

0.7 미만은 힌트다. 소스를 확인한다. 휴리스틱 웹 호출을 런타임 증거로 쓰지 않는다. 신뢰도 0.85 이상인 애플리케이션 그래프 엣지(`RoutesTo`, `Renders`, `BindsTo`, `HandlesEvent`, `Registers`, `ResolvesTo`, `UsesViewModel`)만 자동 처리에 쓴다. 그 엣지는 모호하지 않은 단일 정적 일치일 때만 생긴다.

모호하면 `reason`이 `ambiguous`이고 `matches`에 후보가 있다. 전체 한정 이름이나 `sym://` 안정 ID를 쓴다.

## 한계

JS/TS 동적 호출, 계산된 셀렉터, 패키지 import, 리플렉션, 미들웨어, 소스 생성기, 동적 Razor, DI 팩터리, JS interop은 따라가지 않는다. Razor `@code` 선언은 `.cs` 또는 code-behind로만 해소된다. 상수가 아닌 라우트와 팩터리 DI는 `RoutesTo` / `Registers` / `ResolvesTo`를 만들지 않는다. 최소 API의 람다가 하나의 익명 함수 심볼로 해소되면 `RoutesTo`가 생긴다. 최종 기준은 소스다.

## 종료 코드

| 코드 | 의미 | 처리 |
| ---: | --- | --- |
| 0 | 성공 | 결과를 읽고 필요한 파일을 연다 |
| 1 | 인덱스 없음, 오래된 스키마, 예기치 않은 오류 | 메시지를 보고 인덱스를 만들거나 다시 만든다 |
| 2 | 일치 없음 또는 모호 (`no_matches`, `ambiguous`) | 식별자를 좁히거나 grep으로 넘긴다 |
| 130 | 취소 | 취소로 처리한다 |

## 10. JSON 스키마 — version 4 / version 5

CLI `--json`은 evidence가 없으면 **version 4**, `--evidence`면 **version 5**다.

`--evidence`가 없으면 일반 질의는 version 4다. `--evidence`를 쓰면 관계에 선택 필드가 붙는다.

```json
{
  "location": { "file": "app.ts", "line": 4 },
  "evidence": "static-import"
}
```

evidence 값: `semantic`, `static-import`, `same-file-fallback`, `name-fallback`, `css-selector`, `unknown`.

`--min-confidence`의 기본은 0이다. 유한수가 아니거나 0에서 1 밖이면 `query_failed`다. `relation`은 v5만 쓰므로 `--evidence`가 필수다.

```json
{
  "version": 4,
  "query": "Greet",
  "matches": [],
  "relations": [],
  "stale": false,
  "reason": null
}
```

`context --json`은 `query` 대신 `task`와 `mapLines`를 둔다. 오류도 같은 봉투 버전을 쓰고 `error.code`는 `index_not_found`, `schema_outdated`, `query_failed`, `git_unavailable` 중 하나다.

`codemap diff --json`과 `codemap impact --changed --json`의 필드는 `changedFiles`, `matches`, `relations`, `risk`다.

- `analysisComplete` — git diff 대상 파일이 모두 인덱스에서 해석되면 true
- `unresolvedChangedFiles` — `analysisComplete`가 false일 때 영향 분석되지 않은 변경 파일

`--json`만 쓰면 version 4, `--json --evidence`면 version 5다.

## 11. MCP 설정

```bash
codemap mcp --root <repo>
```

```json
{
  "mcpServers": {
    "codemap": {
      "command": "dnx",
      "args": ["Proofmark.CodeMap@0.3.0", "--yes", "mcp", "--root", "/path/to/repo"]
    }
  }
}
```

nuget.org에 설치한 `codemap` CLI를 쓸 때는 `"command": "codemap"`, `"args": ["mcp", "--root", "..."]`도 가능하다. nuget.org의 `Codemap.Cli`과 `Proofmark.CodeMap`은 다른 패키지다.

도구: `find_symbol`, `get_callers`, `get_callees`, `get_refs`, `get_impl`, `get_flow`, `get_impact`, `get_changed_impact`, `explain_relation`, `get_context`, `get_semantic_slice`, `investigate`, `get_status`, `refresh_index`.

`get_flow`의 기본 `includeEvidence`는 true이나, 첫 조사는 false로 위상만 받는다. `get_impact`의 `profile`은 `code`(참조 엣지) 또는 `app`(거기에 라우트, DI, UI를 더함)이다. 기본 depth는 2, `maxResults`는 20이다.

`codemap lsp`는 한 줄에 JSON 객체 하나인 사용자 정의 브리지다. 표준 LSP 서버가 아니다. 명령은 `find`, `impact`, `context`, `relation`, `flow`다. 응답 `version`은 1이다. 스키마는 [lsp-json-v1.schema.json](lsp-json-v1.schema.json), 현재 CLI는 [cli-json-v4.schema.json](cli-json-v4.schema.json)과 [cli-json-v5.schema.json](cli-json-v5.schema.json)이다.

`codemap status --check-freshness`는 인덱스 파일을 바꾸지 않는다.
