# Proofmark

Proofmark는 변경을 검증할 때 필요한 근거를 모으고, 그 근거만으로 충분한지 결정적으로 판단하는 도구 모음이다.

- **CodeMap**은 변경된 코드와 영향을 받는 범위를 찾는다.
- **Distill**은 빌드·테스트·분석 결과를 증거로 모은다.
- **Proof**는 변경에 필요한 의무가 모두 충족됐는지 판정하고 인증서를 만든다.

세 엔진은 서로 독립적으로 빌드한다. CodeMap은 `engines/codemap`, Distill은 `engines/distill`에 있다. 삭제된 코드의 영향까지 확인하려면 `proof.yml`에서 `analysis.indexBaseRevision: true`를 설정해 기준 리비전의 인덱스를 함께 만들 수 있다.

## 자주 쓰는 명령

- `proof plan`: 변경 집합과 영향, 확인해야 할 의무를 보여준다. 검사는 실행하지 않는다.
- `proof verify`: 변경을 검증하고 인증서를 만든다. 전체 검사는 `--profile full`, JSON 출력은 `--output json`을 사용한다.
- `proof explain`, `proof obligations`, `proof summary`: 최신 인증서의 판정과 근거를 확인한다.
- `proof certificate verify [--require-signed]`: 인증서의 다이제스트와 서명을 다시 검증한다.
- `proof map suggest`, `proof map add`, `proof map from-coverage`: 명시적인 테스트 맵을 관리한다. `--accept`와 `--write`는 사람이 실행하는 명령이다.
- `proof review sign`, `proof review list`: 수동 검토를 기록하고 조회한다. 서명은 사람이 한다.
- `proof history`: 과거 인증서의 분포와 미해결 의무를 요약한다. 과거 인증서는 현재 변경의 증거가 아니다.
- `proof exception list`, `proof exception verify`, `proof exception evaluate`: 서명된 정책 예외를 확인한다. 예외가 인증서의 판정을 바꾸지는 않는다.

Proof MCP는 기본적으로 읽기 전용이다. 검사를 실행하는 `proof_verify` 도구를 등록하려면 `--allow-exec --root <repo>`를 사용한다.

## 문서

- [Proof 도입](docs/adopting-proof.md): 설치, 초기화, MCP와 CI 설정
- [아키텍처](docs/architecture.md): 판정 방식, 의무와 불변 조건
- [로드맵](docs/roadmap.md): 현재 범위와 앞으로 하지 않을 일
- [저장소 설정](docs/repository-settings.md): 브랜치 보호와 필수 검사
