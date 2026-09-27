# CodeMap 아키텍처

```text
Core (그래프, ID, 분석기 계약)
  ├── CSharp ──┐
  ├── Web ─────┼── Storage (SQLite 스키마, 마이그레이션, 질의 저장)
  └── Scip ────┘             ↑
                        Engine (애플리케이션 계약)
                          ↑              ↑
                       CLI             MCP / LSP 브리지
```

`CodeMap.Engine`이 전송과 무관한 경계다. `CodeMapQueryService`와 `IncrementalCodeMapIndexer`는 호환 파사드다. Storage가 지속 그래프를 소유하고, 분석기 프로젝트는 Storage에 의존하지 않는다.

노드는 파일, 선언, 라우트, 마크업, 임포트된 심볼이다. 엣지는 종류, 위치, 해소 종류, 신뢰도를 가진다. 스냅샷과 SQLite 리더는 같은 기본 질의 계약을 구현한다.

지속 메타데이터 상태는 `building`, `updating`, `ready`다. 데이터베이스가 없으면 `missing`이다. 그래프 교체와 최종 `ready`는 한 트랜잭션이다. 읽는 쪽은 갱신 중에도 이전의 완전한 그래프를 본다. 취소되거나 실패한 트랜잭션은 이전 ready 그래프로 롤백한다. 오래된 신선도는 프로세스 로컬이며 그래프를 바꾸지 않는다.

C# 분석은 Roslyn 컴파일을 읽고 선언, 익명 함수, 의미 관계, 공개 표면 지문, 외부 루트를 모은 뒤 ASP.NET, Razor, XAML을 합친다. 웹 분석은 Tree-sitter를 먼저 쓰고, AST에 쓸 결과가 없으면 결정적 정규식으로 떨어진다.

CLI가 v4/v5 JSON과 종료 코드를 소유한다. MCP는 같은 애플리케이션 응답을 도구 봉투로 직렬화한다. `codemap lsp`는 표준 LSP가 아니다. 재인덱스와 스키마 변경이 필요한 버전은 [COMPATIBILITY.md](COMPATIBILITY.md).
