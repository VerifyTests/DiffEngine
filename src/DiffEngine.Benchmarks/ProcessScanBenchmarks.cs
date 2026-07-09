using BenchmarkDotNet.Attributes;

namespace DiffEngine.Benchmarks;

// Compares the process scan that backs ProcessCleanup / DiffRunner's already-running-tool
// detection. Both variants exist in the shipping code: the public unfiltered FindAll() is the
// pre-optimization behavior, and Refresh() is the current filtered behavior.
[MemoryDiagnoser]
public class ProcessScanBenchmarks
{
    // Reads the command line (OpenProcess + several ReadProcessMemory calls on Windows) of
    // every process on the machine.
    [Benchmark(Baseline = true)]
    public int Unfiltered() =>
        ProcessCleanup.FindAll().Count();

    // Reads the command line only for processes whose image name matches a resolved diff tool.
    [Benchmark]
    public int Filtered()
    {
        ProcessCleanup.Refresh();
        return ProcessCleanup.Commands.Count;
    }
}
