# 보안 정책

## 취약점 보고

공개 이슈에 쓰지 않는다. GitHub 저장소의 **Security → Report a vulnerability**(비공개 보안 권고)로 보낸다. 재현 절차, 영향받는 버전(`proof --version`), 증명서나 로그가 있으면 비밀 값을 지운 사본을 붙인다.

지원 버전은 `Version.props`의 최신 릴리스 하나다.

## 서명 키

- `PROOF_ATTESTATION_HMAC_KEY`는 환경 변수로만 읽는다. 증명서, 요약, 로그, `proof.yml`에 키가 들어 있으면 취약점이다. 증명서에는 서명자 신원(`env:PROOF_ATTESTATION_HMAC_KEY`)과 HMAC 값만 있어야 한다.
- HMAC은 대칭 키다. 키를 가진 사람은 누구나 서명할 수 있다. 키는 CI 비밀로만 두고, 회전 절차는 [docs/adopting-proof.md](docs/adopting-proof.md)의 "서명 키 운영"을 따른다.
- 키 없는 서명(Sigstore 등)은 아직 없다. 알 수 없는 증명 제공자는 신뢰하지 않는다.

## 범위 밖

- 보안 스캔과 SBOM은 Proof 의무가 아니다. 스캐너 출력을 증명 증거로 잇지 않고, 이를 위한 규칙(P012)은 Distill 어댑터가 생기기 전에는 만들지 않는다.
- Proof 판정은 변경이 증명 계획을 만족했는지를 말한다. 코드가 안전하다는 보증이 아니다.
