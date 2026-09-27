# Distill

Distill은 .NET 빌드·테스트·분석 결과를 구조화된 증거로 모으고, 필요한 근거를 Failure Pack으로 정리하는 검증 오케스트레이터다. 단순히 로그를 줄이는 도구가 아니라, 검증 결과를 다음 단계에 정확하게 전달하는 도구다.

Distill은 CodeMap이나 Proof를 참조하지 않는다. Proof는 `Proof.Adapters.Distill`을 통해 Distill을 사용한다. Distill 내부에는 LLM을 사용하지 않는다.

```text
dotnet build -bl  →  MSBuild binlog  →  빌드 진단
dotnet test       →  VSTest / MTP     →  테스트 진단
analyzer          →  SARIF             →  분석 진단
git diff          →  변경 hunk
                         ↓
              Distill 진단 IR → Failure Pack
```

신뢰하는 원본은 MSBuild binlog, VSTest 로거(TRX는 폴백), MTP 보고서, SARIF다. stdout 정규식은 다른 방법이 없을 때만 사용한다. 원본 산출물은 버리지 않으며, FAIL 근거가 부족하면 `UNCERTAIN`으로 처리한다. 근거 없이 `PASS`를 만들지는 않는다.

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

`distill.yml`이 없으면 `distill init`을 실행하거나 `distill.yml.example`을 복사한다. `raw`는 stdout/stderr 원문을 그대로 출력한다. 따라서 레드액션이 적용되지 않는 경로임을 알고 사용해야 한다.

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

## 지켜야 하는 원칙

1. CodeMap에 의존하지 않는다.
2. 내부에서 LLM을 사용하지 않는다.
3. 구조화된 산출물이 있으면 텍스트 파싱보다 우선한다.
4. 원본 증거를 모두 보존할 수 있어야 한다.
5. FAIL 근거가 부족하면 `UNCERTAIN`으로 처리한다.
6. 압축률을 높이기 위해 성공 여부를 왜곡하지 않는다.
7. 테스트 플랫폼은 어댑터로 격리한다.
8. 플러그인과 테스트 로거는 얇게 유지한다.
9. 에이전트의 기본 출력 형식은 `compact`다.
10. 완료 전에 `full` 프로파일을 실행할 수 있어야 한다.

MCP 읽기 표면은 `distill_report`, `distill_diagnostics`, `distill_raw`, `distill_doctor`다.
