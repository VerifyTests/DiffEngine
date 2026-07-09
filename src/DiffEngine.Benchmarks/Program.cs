using BenchmarkDotNet.Running;
using DiffEngine.Benchmarks;

// Run with:  dotnet run -c Release --project src/DiffEngine.Benchmarks
// Filter e.g.:  dotnet run -c Release --project src/DiffEngine.Benchmarks -- --filter *ProcessScan*
BenchmarkSwitcher
    .FromTypes([typeof(ProcessScanBenchmarks), typeof(DiffToolsLookupBenchmarks)])
    .Run(args);
