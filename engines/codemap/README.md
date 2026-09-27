# CodeMap

저장소의 로컬 의미 그래프를 만들고, 심볼, 호출자, 호출 대상, 애플리케이션 흐름, 영향, 소스 맥락을 결정적으로 조회한다.

## 입력

- C#과 .NET, ASP.NET Core 포함
- Razor, Blazor, XAML, WPF
- JavaScript, TypeScript, HTML, CSS
- 그 외 언어는 SCIP 그래프 임포트

## 설치와 빠른 시작

```bash
dotnet tool install --global Proofmark.CodeMap --version 0.3.0
cd path/to/repository
codemap index
codemap find OrderService --json
codemap callers "OrdersController.Get" --json
codemap flow "/orders" --kind http --evidence
```

에이전트 기본 순서는 `find` 다음 `flow` 또는 `callers`, 그다음 소스 읽기다. 소스가 바뀌면 `codemap update`를 실행한다. `codemap status --check-freshness`가 인덱스와 신선도를 보고한다.

## 정확도

엣지는 `Semantic`, `Syntactic`, `Heuristic`과 0에서 1 사이의 신뢰도를 보고한다. Roslyn C# 관계는 의미적이다. 마크업과 웹 바인딩은 대개 구문적이거나 휴리스틱이다. 지원하지 않는 구문, 생성 코드, 동적 디스패치, 로컬이 아닌 빌드 설정은 해소되지 않을 수 있다.

CLI JSON은 SQLite 스키마와 따로 버전이 매겨진다. MCP와 JSON 줄 LSP 브리지는 같은 애플리케이션 의미를 쓰고 전송 봉투만 다르다.

## 개발

[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), [docs/COMPATIBILITY.md](docs/COMPATIBILITY.md), [docs/AGENT_INTEGRATION.md](docs/AGENT_INTEGRATION.md).

```bash
dotnet restore CodeMap.slnx
dotnet build CodeMap.slnx --no-restore -warnaserror
dotnet test CodeMap.slnx --no-build
```

스냅샷과 SQLite 질의 동등, 분석기 해소 의미, 공개 CLI/MCP 계약을 유지한다.
