using BenchmarkDotNet.Attributes;

// What the form does with the screen it is handed on a frame in which nothing happened, which is
// nearly every frame: sixty a second for as long as the window is up. The form is never shown, so
// nothing is painted and nothing appears on the desktop: this is only the deciding that there is
// nothing to do.
[MemoryDiagnoser]
public class IdleFrameBenchmarks
{
    ViewerForm form = null!;
    Screen first = null!;
    Screen second = null!;
    bool flip;

    [GlobalSetup]
    public void Setup()
    {
        ViewerApp.ConfigureUnscaled();
        var directory = Path.Combine(Path.GetTempPath(), "deview-benchmarks", "idle");
        var state = SessionState.Start(ViewerMode.Inline);
        for (var index = 0; index < 500; index++)
        {
            state = ViewerSession.EnqueueInline(
                state,
                new(Path.Combine(directory, $"Sample{index % 40}Tests.cs"), 10 + index, "\"old\"", $"new {index}")
                {
                    TestName = $"Test{index}"
                });
        }

        // Two screens that say the same thing, as two builds of one state do
        first = ScreenBuilder.Build(state);
        second = ScreenBuilder.Build(state);
        form = new("benchmark", 1100, 700);
        form.Apply(first);
    }

    [GlobalCleanup]
    public void Cleanup() =>
        form.Dispose();

    // The screen the loop handed over last frame, which is what it hands over while the state is
    // the one it was.
    [Benchmark]
    public void TheScreenOfTheFrameBefore() =>
        form.Apply(first);

    // A screen built again from the same state: equal in everything and another object, which is
    // what every idle frame handed over while a screen was built per frame.
    [Benchmark]
    public void AnEqualScreenBuiltAgain()
    {
        flip = !flip;
        form.Apply(flip ? second : first);
    }
}
