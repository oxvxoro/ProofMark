# 골든 Failure Pack 픽스처

`CompactFailurePackFormatter` 출력의 종단 스냅샷이다. 시나리오 디렉터리의 `failure-pack.txt`가 기대하는 L3 Failure Pack이다.

`tests/Distill.Tests/Golden/GoldenFailurePackTests.cs`가 합성 검사 결과와 git hunk로 `VerificationReportBuilder`를 돌리고, 정규화한 텍스트를 이 파일과 비교한다.

| 시나리오 | 내용 |
| --- | --- |
| `build-failure/` | 변경 hunk와 연관된 MSBuild 오류 |
| `test-failure/` | VSTest 로거 실패와 빌드 통과 |
| `insufficient-fail/` | 구조화 진단 없는 FAIL과 원문 꼬리 |
| `analysis-failure/` | SARIF 분석 진단 |
| `redaction/` | AI에 보이는 비밀 레드액션 |

포매터를 의도적으로 바꾼 뒤 스냅샷을 갱신하려면 해당 `failure-pack.txt`를 지우고, `GoldenText.AssertMatchesGolden`에서 생성을 잠시 허용하거나, 경로를 정규화한 로컬 `distill verify` 결과로 복사한다.
