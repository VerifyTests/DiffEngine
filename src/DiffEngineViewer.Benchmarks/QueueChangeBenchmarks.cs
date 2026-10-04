using BenchmarkDotNet.Attributes;

// What one change to a long queue costs the session: a snapshot arriving, a test failing again
// with something else, a test that started passing, and one accept. Each is done under the lock
// the render loop takes, on the listener thread a test process is waiting on, and each is about
// one entry of the queue however many there are. The applier here answers at once and touches
// nothing, so everything measured is the session's own work.
[MemoryDiagnoser]
public class QueueChangeBenchmarks
{
    [Params(200, 2000)]
    public int Entries;

    SessionState state = null!;
    InlinePatch arriving = null!;
    InlinePatch again = null!;
    string settling = "";

    static readonly ViewerActions applied = new(
        static _ => InlineApplyResult.Applied,
        static (_, _) =>
        {
        },
        static _ =>
        {
        });

    [GlobalSetup]
    public void Setup()
    {
        // Nothing here is read: an inline entry is built from its patch, and the directory only has
        // to be somewhere with no solution above it.
        var directory = Path.Combine(Path.GetTempPath(), "deview-benchmarks", "change");
        var built = SessionState.Start(ViewerMode.Inline, 160, 50);
        for (var index = 0; index < Entries; index++)
        {
            built = ViewerSession.EnqueueInline(built, Patch(directory, index, $"new {index}"));
        }

        // On an entry in the middle, as a reader part way through a queue is
        state = ViewerSession.Apply(built, Command.Select(Entries / 2));
        arriving = Patch(directory, Entries, "new");
        again = Patch(directory, Entries / 4, "something else this time");
        settling = InlineKey.For(again.SourceFile, again.LineHint);
    }

    static InlinePatch Patch(string directory, int index, string content) =>
        new(Path.Combine(directory, $"Sample{index % 40}Tests.cs"), 10 + index, "\"old\"", content)
        {
            TestName = $"Test{index}",
            MemberName = $"Test{index}",
            Framework = "net10.0"
        };

    [Benchmark]
    public object ASnapshotArrives() =>
        Changed(ViewerSession.EnqueueInline(state, arriving), Entries + 1);

    [Benchmark]
    public object ATestFailsAgainWithSomethingElse() =>
        Changed(ViewerSession.EnqueueInline(state, again), Entries);

    [Benchmark]
    public object ATestStartsPassing() =>
        Changed(ViewerSession.Settle(state, settling, "net10.0", again.MemberName), Entries - 1);

    [Benchmark]
    public object OneAccept() =>
        Changed(ViewerSession.Apply(state, CommandKind.Accept, applied), Entries - 1);

    static SessionState Changed(SessionState changed, int count)
    {
        if (changed.Queue.Count != count)
        {
            throw new($"{changed.Queue.Count} entries where {count} were expected: {changed.Message}");
        }

        return changed;
    }
}
