using BenchmarkDotNet.Running;

namespace Proof.Benchmarks;

// BenchmarkDotNet 진입점. 스위트 목록:
//   dotnet run -c Release --project benchmarks/Proof.Benchmarks -- --list flat
// 스위트 하나 실행:
//   dotnet run -c Release --project benchmarks/Proof.Benchmarks -- --filter *PlanningBenchmarks*
public static class Program
{
    public static void Main(string[] args)
        => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
