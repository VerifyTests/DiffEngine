using BenchmarkDotNet.Attributes;

namespace DiffEngine.Benchmarks;

// Guards the per-lookup cost and allocations of the DiffTools resolution paths, which are keyed
// by a dictionary and a cached first-text-tool rather than per-call LINQ scans.
[MemoryDiagnoser]
public class DiffToolsLookupBenchmarks
{
    [Benchmark]
    public bool ByEnum() =>
        DiffTools.TryFindByName(DiffTool.VisualStudio, out _);

    [Benchmark]
    public bool ForText() =>
        DiffTools.TryFindForText(out _);

    [Benchmark]
    public bool ByTextExtension() =>
        DiffTools.TryFindByExtension(".txt", out _);

    [Benchmark]
    public bool ByBinaryExtension() =>
        DiffTools.TryFindByExtension(".png", out _);

    [Benchmark]
    public bool IsDetected() =>
        DiffTools.IsDetectedForFile(DiffTool.VisualStudio, "input.txt");
}
