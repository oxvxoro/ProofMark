# Proofmark 에이전트 지침

## 언어

주석, 문서, 커밋 메시지, 사용자에게 보이는 설명은 한글로 작성한다. 식별자, CLI, 설정 키, 오류 코드, 테스트가 기대하는 메시지 문자열은 영어를 유지한다.

## 저장소 안전

- 모델 판단으로 증명 증거, 판정, 정책 변경을 만들지 않는다.
- `proof review sign`과 `proof map add --accept`는 사람이 작성하는 명령이다. 실행하지 않는다.
- 사람이 명시하지 않으면 커밋, 푸시, 리셋, 변경 폐기를 하지 않는다.
- 요청한 변경 밖의 파일을 되돌리거나 체크아웃하거나 스태시하지 않는다.

## 검증

Proofmark 변경의 기본 확인:

```text
dotnet build Proof.slnx --no-restore
dotnet test tests/Proof.Tests --no-build
```

엔진을 건드렸으면 해당 솔루션 테스트도 실행한다.

```text
dotnet test engines/codemap/CodeMap.slnx --no-build
dotnet test engines/distill/Distill.sln --no-build
```

변경이 Proof 검증을 요구하면 `proof plan` / `proof verify`를 실행한다.

`dotnet test Proof.slnx`는 기본 게이트가 아니다. 전체 솔루션이 필요할 때만, 시간을 제한해서 실행한다.

명령을 쓸 수 없거나 환경이 부족해 실패하면, 그 명령과 실패 내용을 그대로 알린다.
