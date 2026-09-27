# Distill

.NET 빌드, 테스트, 분석 결과를 구조화된 증거로 모으고, 증거가 남는 Failure Pack으로 줄이는 검증 오케스트레이터다. 로그를 짧게 만드는 것이 목적이 아니다. 검증 근거를 처음부터 정확히 넘기는 것이 목적이다.

CodeMap, Proof와 서로 참조하지 않는다. Proof는 `Proof.Adapters.Distill`로 Distill을 쓴다. Distill 안에서 LLM을 쓰지 않는다.

```text
dotnet build -bl  →  MSBuild binlog  →  빌드 진단
dotnet test       →  VSTest / MTP     →  테스트 진단
analyzer          →  SARIF             →  분석 진단
git diff          →  변경 hunk
                         ↓
              Distill 진단 IR → Failure Pack
```

정본은 MSBuild binlog, VSTest 로거(TRX는 폴백), MTP 보고서, SARIF다. stdout 정규식은 마지막 수단이다. 원문 산출물을 버리지 않는다. FAIL 근거가 부족하면 `UNCERTAIN`이다. 거짓 `PASS`는 없다.

## 명령

```bash
dotnet run --project src/Distill.Cli -- init
dotnet run --project src/Distill.Cli -- verify --profile quick --output compact
dotnet run --project src/Distill.Cli -- verify --profile full --output compact
dotnet run --project src/Distill.Cli -- doctor
dotnet run --project src/Distill.Cli -- report
dotnet run --project src/Distill.Cli -- diagnostics --kind test
dotnet run --project src/Distill.Cli -- raw unit --tail 200
```

`distill.yml`이 없으면 `distill init` 또는 `distill.yml.example` 복사를 쓴다. `raw`는 원문 stdout/stderr를 그대로 낸다. 레드액션을 적용하지 않는 탈출구다.

골든 Failure Pack:

```bash
dotnet test tests/Distill.Tests --filter GoldenFailurePackTests
```

픽스처는 `fixtures/golden/`이다. build-failure, test-failure, insufficient-fail, analysis-failure, redaction.

## 종료 코드

| 코드 | 의미 |
| ---: | --- |
| 0 | PASS |
| 1 | FAIL |
| 2 | 설정 오류 |
| 3 | 인프라 오류 |
| 4 | UNCERTAIN |
| 130 | 취소 |

## 불변 조건

1. CodeMap에 의존하지 않는다.
2. 내부에서 LLM을 쓰지 않는다.
3. 구조화된 산출물이 있으면 텍스트 파싱보다 우선한다.
4. 원문 증거를 전부 버리지 않는다.
5. FAIL 증거가 부족하면 `UNCERTAIN`이다.
6. 압축률을 위해 성공률을 낮추지 않는다.
7. 테스트 플랫폼은 어댑터로 격리한다.
8. 플러그인과 테스트 로거는 얇게 유지한다.
9. 에이전트 기본 출력은 compact다.
10. 완료 전에 full 프로파일을 실행할 수 있다.

MCP 읽기 표면은 `distill_report`, `distill_diagnostics`, `distill_raw`, `distill_doctor`다.
