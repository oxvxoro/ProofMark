---
name: proof
description: Proofmark 변경을 검증하거나 인증서와 의무를 읽을 때 사용한다. Distill PASS는 Proof PROVEN이 아니다. proof map add --accept와 proof review sign은 실행하지 않는다.
---

# Proof

Proof는 테스트가 통과했는지를 묻지 않는다. 변경에 대한 증거가 충분한지를 묻는다.

## 절차

1. `proof plan` — 변경 집합, 영향, 의무. Distill은 실행하지 않는다.
2. `proof verify` — Distill을 실행하고 `.proof/certificates`에 인증서를 쓴다.
3. `proof explain` / `proof obligations` / `proof summary` — 최신 인증서를 읽는다.

심볼, 호출자, 영향은 CodeMap MCP를 쓴다. 실패한 verify 뒤의 Failure Pack은 Distill MCP(`distill_report`, `distill_diagnostics`)를 쓴다.

## 규칙

- 모델 판단으로 증거를 만들거나 의무를 닫지 않는다.
- P005와 P010은 명시적 `policy.testMaps` 또는 주제가 맞는 런타임 커버리지만으로 닫힌다. 호출자 관계로는 닫히지 않는다.
- MCP 기본은 읽기 전용이다. `proof_verify`는 `--allow-exec`가 있을 때만 나타난다. 작성 명령은 터미널에 남긴다.
