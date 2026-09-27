# 저장소 설정 (유지보수자)

`master`를 보호하는 사람 작업이다. 에이전트는 브랜치 보호와 필수 검사를 바꾸지 않는다.

## 필수 상태 검사

`.github/workflows/ci.yml`의 다음 잡은 `master` 머지 전에 모두 필수다.

- `codemap`
- `distill`
- `distill-path`
- `proof`
- `proof-windows`
- `proof-full`
- `pack`

`proof`, `proof-windows`, `proof-full`이 인증서를 만들고 검증한다. `pack`은 패키징된 도구로 독립 소비자 저장소의 판정 `PROVEN`과 두 MCP 스모크를 요구한다.

## 규칙 집합

Settings → Rules → Rulesets, 또는 관리 권한이 있는 토큰:

```bash
gh api --method POST repos/OWNER/REPO/rulesets \
  -H "Accept: application/vnd.github+json" \
  --input docs/repository-ruleset.json
```

`docs/repository-ruleset.json`의 `bypass_actors`는 적용 전에 팀 정책에 맞춘다. `OWNER/REPO`는 API 경로에 넣는다.

## 고전적 브랜치 보호

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

사람 리뷰를 필수로 할지는 팀 절차다. 예제의 `required_pull_request_reviews`는 `null`이다.

## 메모

- 포크 PR에는 `PROOF_ATTESTATION_HMAC_KEY`가 없다. 복합 액션은 서명 없는 봉투를 허용한다. 필수 검사 목록은 같다.
- `master`로의 검사 없는 직접 푸시는 허용하지 않는다.
- `.proof-e2e.*`는 gitignore된다. 로컬 e2e가 `git status`를 더럽히지 않는다.
- `allow-exceptions: true`이면 액션이 `proof exception evaluate --gate`를 실행한다. 열린 필수 의무가 모두 유효한 서명 예외로 덮인 의무 주도 차단만 머지 차단 종료 코드를 지운다. 차단 제약과 종료 코드 2/4는 지워지지 않고, 인증서 판정도 바뀌지 않는다.
