# 저장소 설정 (유지보수자용)

이 문서는 `master` 브랜치 보호와 필수 검사 설정을 담당하는 유지보수자를 위한 안내다. 에이전트는 브랜치 보호나 필수 검사 설정을 변경하지 않는다.

## 필수 상태 검사

`.github/workflows/ci.yml`의 다음 잡은 `master`에 머지하기 전에 모두 통과해야 한다.

- `codemap`
- `distill`
- `distill-path`
- `proof`
- `proof-windows`
- `proof-full`
- `pack`

`proof`, `proof-windows`, `proof-full`은 인증서를 만들고 검증한다. `pack`은 패키징한 도구를 독립 소비자 저장소에 설치한 뒤 `PROVEN` 판정과 두 MCP 스모크 테스트가 통과하는지 확인한다.

## 규칙 집합

GitHub에서 Settings → Rules → Rulesets로 이동해 설정하거나, 관리 권한이 있는 토큰으로 다음 명령을 실행한다.

```bash
gh api --method POST repos/OWNER/REPO/rulesets \
  -H "Accept: application/vnd.github+json" \
  --input docs/repository-ruleset.json
```

적용하기 전에 `docs/repository-ruleset.json`의 `bypass_actors`를 팀 정책에 맞게 수정한다. `OWNER/REPO`는 실제 저장소 경로로 바꾼다.

## 기존 브랜치 보호 API 사용

```bash
gh api --method PUT repos/OWNER/REPO/branches/master/protection \
  -H "Accept: application/vnd.github+json" \
  --input - <<'JSON'
{
  "required_status_checks": {
    "strict": true,
    "contexts": ["codemap", "distill", "distill-path", "proof", "proof-windows", "proof-full", "pack"]
  },
  "enforce_admins": true,
  "required_pull_request_reviews": null,
  "restrictions": null
}
JSON
```

사람 리뷰를 필수로 지정할지는 팀 절차에 따라 결정한다. 예제에서는 `required_pull_request_reviews`를 `null`로 둔다.

## 메모

- 포크 PR에서는 `PROOF_ATTESTATION_HMAC_KEY`를 사용할 수 없다. 복합 액션은 서명되지 않은 봉투를 허용하지만 필수 검사 목록은 동일하다.
- 검사 없이 `master`에 직접 푸시할 수 없도록 설정한다.
- `.proof-e2e.*`는 gitignore 대상이다. 로컬 e2e 실행 결과가 `git status`에 나타나지 않는다.
- `allow-exceptions: true`이면 액션이 `proof exception evaluate --gate`를 실행한다. 열린 필수 의무가 모두 유효한 서명 예외로 덮였을 때 의무 때문에 발생한 머지 차단만 해제한다. 차단 제약과 종료 코드 2·4는 해제하지 않으며, 인증서 판정도 바꾸지 않는다.
