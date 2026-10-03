using System.Text;
using BenchmarkDotNet.Attributes;

// "Accept all for" a test, clicked in a window that owns its queue: every snapshot of the group
// written into one source file by the real applier. The first number is how long the click holds
// the render thread, under the lock every arrival and listing waits on. The second is how long
// until the last snapshot is written, whoever writes it.
//
// Each round leaves the file ready for the next: the snapshots go from one set of literals to the
// other and then back, so every invocation applies every patch for real and nothing has to be put
// back in between.
[MemoryDiagnoser]
public class GroupAcceptBenchmarks
{
    const int columns = 160;
    const int rows = 50;

    [Params(20, 200)]
    public int Entries;

    string directory = "";
    SessionState forward = null!;
    SessionState back = null!;
    bool flipped;
    string accepted = "";
    readonly IViewerWindow window = new NoWindow();

    // The second item of a header's menu, after the fold.
    static readonly ViewerInput click = new(CommandKind.None, -1, -1, 0, false, columns, rows)
    {
        ClickedMenuItem = 1
    };

    [GlobalSetup]
    public void Setup()
    {
        directory = Path.Combine(Path.GetTempPath(), "deview-benchmarks", $"group-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "BenchTests.cs");
        File.WriteAllText(source, Source());
        forward = Queue(source, "a", "b");
        back = Queue(source, "b", "a");
        flipped = false;
        accepted = $"Accepted {Entries}";
    }

    [GlobalCleanup]
    public void Cleanup() =>
        Directory.Delete(directory, true);

    [Benchmark]
    public void WindowHeldByTheClick()
    {
        var state = ViewerProgram.Apply(Next(), click, null, window);
        // Either the click applied everything, or it began a batch for a worker to apply.
        if (state.Batch is null &&
            state.Message != accepted)
        {
            throw new($"The click ended with: {state.Message}");
        }
    }

    [Benchmark]
    public void ClickUntilEverySnapshotIsWritten()
    {
        var host = new SessionHost(Next());
        host.Mutate(_ => ViewerProgram.Apply(_, click, null, window));
        // What the loop starts on a worker when a click leaves a batch behind. Nothing to do when
        // the click applied everything itself.
        new AcceptAllRunner(host, ViewerActions.Real).Drive();
        if (host.State.Message != accepted)
        {
            throw new($"The batch ended with: {host.State.Message}");
        }
    }

    SessionState Next()
    {
        flipped = !flipped;
        return flipped ? forward : back;
    }

    SessionState Queue(string source, string from, string to)
    {
        var state = SessionState.Start(ViewerMode.Inline, columns, rows);
        for (var index = 0; index < Entries; index++)
        {
            state = ViewerSession.EnqueueInline(
                state,
                new(source, 6 + index, $"\"{from}{index}\"", $"{to}{index}")
                {
                    TestName = "Bench",
                    MemberName = "Bench"
                });
        }

        // One file and one test, so the queue is one group under one header, which is row 0.
        var open = ViewerSession.OpenMenu(state, 0);
        if (open.Menu?.Items[click.ClickedMenuItem].Kind != CommandKind.AcceptGroup)
        {
            throw new("The menu over the first row has no group accept where the click lands.");
        }

        return open;
    }

    string Source()
    {
        var builder = new StringBuilder();
        builder.Append("public class BenchTests\n{\n    [Test]\n    public async Task Bench()\n    {\n");
        for (var index = 0; index < Entries; index++)
        {
            builder.Append($"        await Verify(value{index}).Snapshot(\"a{index}\");\n");
        }

        builder.Append("    }\n}\n");
        return builder.ToString();
    }

    sealed class NoWindow : IViewerWindow
    {
        public bool Present(Screen screen) =>
            true;

        public ViewerInput Poll() =>
            default;

        public void SetHidden(bool hidden)
        {
        }

        public void SetClipboard(string text)
        {
        }

        public void Focus()
        {
        }

        public bool Capture(Screen screen, int width, int height, string pngPath) =>
            false;

        public void Dispose()
        {
        }
    }
}
