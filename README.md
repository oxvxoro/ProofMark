# Proof

변경의 증거가 충분한지 결정적으로 검증한다. CodeMap이 영향을 보고, Distill이 증거를 모으며, Proof가 충분성을 판정한다.

엔진은 `engines/codemap`, `engines/distill`에서 독립적으로 빌드된다. 삭제 영향은 기준 그래프에서 추적한다. 갱신 전 인덱스가 없으면 `proof.yml`의 `analysis.indexBaseRevision: true`로 기준 리비전을 인덱싱한다.

## 명령

- `proof verify` — 변경을 검증하고 인증서를 쓴다 (`--profile full`, `--output json`)
- `proof plan` — 변경 집합, 영향, 의무. 검사는 실행하지 않는다
- `proof map suggest` / `proof map add --symbol <s> --test <t> --accept` / `proof map from-coverage` — 명시적 테스트 맵 (P005). `--accept`는 사람만
- `proof review sign --subject <path>` / `proof review list` — 수동 검토 (P009). 서명은 사람만
- `proof explain` / `proof obligations` / `proof summary` — 최신 인증서 조회
- `proof certificate verify [--require-signed]` — 다이제스트와 서명을 다시 계산한다. `--repository`, `--allow-ref`, `--allow-workflow`, `--expected-commit-sha`
- `proof history [--rule P005] [--format json]` — 과거 인증서 요약. 분석일 뿐 증거가 아니다
- `proof exception list` / `verify` / `evaluate --certificate <path>` — 서명된 정책 예외. 판정을 바꾸지 않는다
- `dotnet run --project src/Proof.Mcp` — Proof MCP. 기본은 읽기 전용. `proof_verify`는 `--allow-exec --root <repo>`

## 문서

- [docs/adopting-proof.md](docs/adopting-proof.md) — 설치, 초기화, CI
- [docs/architecture.md](docs/architecture.md) — 의무, 판정, 불변 조건
- [docs/roadmap.md](docs/roadmap.md) — 남은 범위와 하지 않는 일
- [docs/repository-settings.md](docs/repository-settings.md) — 브랜치 보호와 필수 검사
# oxvxoro-Proofmark
