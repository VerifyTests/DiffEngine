using BenchmarkDotNet.Attributes;

// One pass of the watch that keeps an owned queue's pending files in step with the disk. It runs
// five times a second for as long as the viewer does, which with a tray installed is for days, so
// what a pass costs is paid whether or not anybody is looking. Real files, since the cost is the
// stat: two per pending move.
[MemoryDiagnoser]
public class TrackedWatchBenchmarks
{
    [Params(100, 1000)]
    public int Pending;

    string directory = "";
    TrackedWatch watch = null!;

    [GlobalSetup]
    public void Setup()
    {
        directory = Path.Combine(Path.GetTempPath(), "deview-benchmarks", $"watch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var state = SessionState.Start(ViewerMode.Inline, 160, 50);
        for (var index = 0; index < Pending; index++)
        {
            var received = Path.Combine(directory, $"Sample{index}.received.txt");
            var verified = Path.Combine(directory, $"Sample{index}.verified.txt");
            File.WriteAllText(received, $"received {index}");
            File.WriteAllText(verified, $"verified {index}");
            state = ViewerSession.EnqueueTracked(state, TrackedEntry.ForMove(received, verified));
        }

        if (state.Queue.Count != Pending)
        {
            throw new($"Queued {state.Queue.Count} of {Pending}.");
        }

        watch = new(new(state));
    }

    [GlobalCleanup]
    public void Cleanup() =>
        Directory.Delete(directory, true);

    // Nothing on disk changes between passes, which is every pass but a handful.
    [Benchmark]
    public void OnePassWithNothingChanged() =>
        watch.Pump();
}
