# 남은 범위

0.3에서 배포, `proof init`, 소비자 CI, Distill·Proof MCP 읽기 표면을 구현했다. 현재 남아 있는 작업은 다음과 같다.

## 0.4 정직한 완전성

새 규칙 ID를 추가하지 않고, 현재 규칙의 적용 범위를 정확하게 넓힌다.

- `policy.architecture: required`는 필수 cycle/layer 위반과 분석 불완전 상태를 차단한다. 저장소 설정은 required이며, CI 인증서 assertion이 P011 직접 증거를 확인한다.
- 이 저장소에서는 P010을 켜지 않는다. Proofmark 자체는 웹 애플리케이션이 아니기 때문이다.

## 0.5 폴리글롯

새 언어 분석기를 추가하는 대신, 현재 제공하는 SCIP 임포트를 정확하게 사용한다.

- `.codemap/scip-providers.json`에 선언되고 파일이 있는 SCIP 산출물은 해시가 바뀌면 `UpdateAsync`, `index`, `index --force`에서 다시 읽힌다. 선언이 없거나 파일이 없으면 스테일 예외가 난다. 솔루션 재조정은 `scip:` 프로젝트를 지우지 않는다.
- SCIP는 `References`를 낸다. 호출자 조회는 `Calls`와 `scip:` 프로젝트의 semantic `References`를 읽고, 저장 엣지는 `References` 그대로다. C# 타입 이름 `References`는 호출자가 아니다. P002는 여전히 테스트 호출자만 센다. `scip:` 프로젝트를 테스트로 분류하는 규칙이 없으므로 비-.NET P002는 아직 비어 있을 수 있다.
- `kind: process`는 첫 토큰을 실행 파일로 실행하고 종료 코드 증거만 만든다. 이 증거는 어떤 의무에도 direct로 붙지 않는다.
- `source: junit` + `project: scip:{name}` 검사는 JUnit XML을 테스트 케이스 증거로 내고, 그 프로젝트의 P004/P002만 덮는다. `coverage:` 경로의 Cobertura는 이 실행에서 쓰였을 때만 P005 커버리지 생산자로 간다. 없으면 P005는 미해결이다. 언어별 러너는 없다. 커버리지만 있는 process 검사는 선택되지 않는다.

## 현재 계획에 없는 일

- 맵 항목을 자동으로 수락하지 않는다. `proof map add --accept`는 사람이 실행한다.
- 호출자 관계만으로 P005 또는 P010을 닫지 않는다.
- 판정 경로에 LLM을 넣지 않는다.
- CodeMap과 Distill이 서로 또는 Proof를 참조하도록 만들지 않는다.
- 부족한 증거를 조용히 `Pass`로 처리하지 않는다.
- Sigstore, 표준 LSP, Visual Studio 확장, P012 이상의 규칙 ID는 현재 계획에 없다.
