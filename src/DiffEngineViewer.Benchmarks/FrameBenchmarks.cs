using BenchmarkDotNet.Attributes;

// What a frame costs before a head is handed it. The loop presents sixty times a second, ten when
// the window is hidden, and the screen it presents is a function of the session state alone, which
// only changes when something happens. So this is paid once per change at best, and was paid once
// per frame: a window nobody is touching built its whole queue's labels, groups and tooltips sixty
// times a second to draw the forty rows that fit.
[MemoryDiagnoser]
public class FrameBenchmarks
{
    [Params(100, 500, 2000)]
    public int Entries;

    SessionState state = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Nothing here is read: an inline entry is built from its patch, and the directory only has
        // to be somewhere with no solution above it.
        var directory = Path.Combine(Path.GetTempPath(), "deview-benchmarks", "frame");
        var built = SessionState.Start(ViewerMode.Inline, 160, 50);
        for (var index = 0; index < Entries; index++)
        {
            built = ViewerSession.EnqueueInline(
                built,
                new(Path.Combine(directory, $"Sample{index % 40}Tests.cs"), 10 + index, "\"old\"", $"new {index}")
                {
                    TestName = $"Test{index}",
                    Framework = "net10.0"
                });
        }

        state = built;
    }

    readonly ScreenCache screens = new();

    // One screen from one state: what every frame paid, and what a change to the state that leaves
    // the queue the list it was still does. A scroll and a drag are that, every frame they last.
    [Benchmark(Baseline = true)]
    public object BuildScreen() =>
        ScreenBuilder.Build(state);

    // And the screen of a queue that is another list, which is what an arrival, an accept or a
    // settle leaves: the labels are worked out again, over every entry.
    [Benchmark]
    public object BuildScreenOfAChangedQueue() =>
        ScreenBuilder.Build(state with { Queue = [..state.Queue] });

    // What the loop asks for each frame now: the screen of the state, which is the one in hand
    // whenever the state is the one the last frame had.
    [Benchmark]
    public object FrameWithNothingChanged() =>
        screens.For(state);
}
