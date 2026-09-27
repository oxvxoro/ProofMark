# CodeMap 벤치마크

Tier A는 질의와 갱신 경로다. Tier B는 그래프, SCIP, 외부 어셈블리, 의미 슬라이스, 신선도다. 그래프 말뭉치는 필요할 때 만든다.

```bash
python3 benchmarks/generate_fixture.py /tmp/codemap-generated-medium --projects 50 --symbols 50000
dotnet run -c Release --project benchmarks/CodeMap.Benchmarks -- --filter '*FindBenchmarks*'
```

산출물에는 `formatVersion`, 커밋, 런타임, OS/CPU, 그래프 크기, 경우별 `medianMs`, 선택적 `p95Ms`, `allocatedBytes`, `correct`가 있다. 비교 스크립트는 정확성과 2배에 닿는 할당에서 실패한다. 벽시계 변화는 러너가 안정되기 전까지 경고다.
