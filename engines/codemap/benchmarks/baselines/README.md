# 벤치마크 기준선

기준선 JSON은 통과/실패 시간이 아니다. 런마다 런타임, OS, CPU, 커밋, 그래프 크기, 중앙값, 선택적 p95, 할당 바이트를 기록한다. Tier A 마이크로벤치마크는 풀 리퀘스트에 맞고, Tier B 전체 실행은 주간이다. 정확성은 하드 게이트다. 벽시계와 할당 변화는 안정된 러너가 생기기 전까지 리뷰용으로 보고한다.

```bash
dotnet run -c Release --project benchmarks/CodeMap.Benchmarks -- --filter '*'
python3 benchmarks/quota_ab.py
```
