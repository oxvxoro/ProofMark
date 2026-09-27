# Proof 벤치마크

플래너, 증거 바인더, git blob 해시의 BenchmarkDotNet 스위트다. 최적화의 전후를 반복 측정하려고 두었고, 단위 테스트에서 단정하지 않는다. 직접 실행한다.

```bash
dotnet run -c Release --project benchmarks/Proof.Benchmarks -- --list flat
dotnet run -c Release --project benchmarks/Proof.Benchmarks -- --filter "*PlanningBenchmarks*"
```

- `PlanningBenchmarks` — 변경 심볼 10/100/1000, 영향 심볼 1k/10k, 관계 10k/100k.
- `BindingBenchmarks` — 의무 100/500/1000 대 증거 1k/10k/50k. 증거의 95%는 무관한 `TestCase`.
- `GitHashBenchmarks` — 10/100/500 MiB의 옛 blob을 `GitCatFileBatchHasher`로 흘린다. 할당은 blob 전체가 아니라 고정 버퍼를 따라야 한다.

`[MemoryDiagnoser]`가 경우마다 Mean, Allocated, Gen0/1/2를 보고한다. `Proof.slnx`의 `/proof/benchmarks/`에 등록되어 빌드에는 포함되지만 테스트 게이트에는 들어가지 않는다.
