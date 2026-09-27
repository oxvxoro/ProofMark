# CodeMap

CodeMap은 저장소의 로컬 의미 그래프를 만든다. 이 그래프를 사용하면 심볼의 위치, 호출자와 호출 대상, 애플리케이션 흐름, 변경 영향, 관련 소스 맥락을 일관되게 조회할 수 있다.

## 지원하는 입력

- C#과 .NET, ASP.NET Core 포함
- Razor, Blazor, XAML, WPF
- JavaScript, TypeScript, HTML, CSS
- 그 외 언어는 SCIP 그래프를 임포트한다.

## 설치와 빠른 시작

```bash
dotnet tool install --global Proofmark.CodeMap --version 0.4.0
cd path/to/repository
codemap index
codemap find OrderService --json
codemap callers "OrdersController.Get" --json
codemap flow "/orders" --kind http --evidence
```

에이전트는 보통 `find`로 대상을 찾은 뒤 `flow` 또는 `callers`로 관계를 확인하고 소스를 읽는다. 소스를 변경한 뒤에는 `codemap update`를 실행한다. 인덱스 상태와 최신 여부는 `codemap status --check-freshness`로 확인할 수 있다.

## 정확도

각 엣지는 `Semantic`, `Syntactic`, `Heuristic` 중 하나의 해소 종류와 0에서 1 사이의 신뢰도를 가진다. Roslyn으로 분석하는 C# 관계는 의미적이며, 마크업과 웹 바인딩은 대개 구문적이거나 휴리스틱이다. 지원하지 않는 구문, 생성 코드, 동적 디스패치, 로컬에 없는 빌드 설정은 해소하지 못할 수 있다.

CLI JSON 버전은 SQLite 스키마 버전과 별도로 관리한다. MCP와 JSON 줄 LSP 브리지는 같은 애플리케이션 의미를 사용하고 전송 봉투만 다르게 표현한다.

## 개발

[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), [docs/COMPATIBILITY.md](docs/COMPATIBILITY.md), [docs/AGENT_INTEGRATION.md](docs/AGENT_INTEGRATION.md).

```bash
dotnet restore CodeMap.slnx
dotnet build CodeMap.slnx --no-restore -warnaserror
dotnet test CodeMap.slnx --no-build
```

스냅샷과 SQLite 질의의 동등성, 분석기 해소 의미, 공개 CLI·MCP 계약을 유지해야 한다.
