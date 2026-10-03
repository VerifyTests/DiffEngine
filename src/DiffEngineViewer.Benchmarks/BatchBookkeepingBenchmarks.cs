using BenchmarkDotNet.Attributes;

// What a bulk accept costs apart from the applying: the claims, the records and the queue rebuilt
// after each. The applier here answers at once and touches nothing, so everything measured is the
// session's own work, which is done under the lock the render loop takes.
[MemoryDiagnoser]
public class BatchBookkeepingBenchmarks
{
    [Params(200, 2000)]
    public int Entries;

    SessionState all = null!;
    SessionState group = null!;

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
        var directory = Path.Combine(Path.GetTempPath(), "deview-benchmarks", "bookkeeping");
        var state = SessionState.Start(ViewerMode.Inline, 160, 50);
        for (var index = 0; index < Entries; index++)
        {
            state = ViewerSession.EnqueueInline(
                state,
                new(Path.Combine(directory, "BenchTests.cs"), 10 + index, $"\"old{index}\"", $"new {index}")
                {
                    TestName = "Bench",
                    MemberName = "Bench"
                });
        }

        all = state;
        // One file and one test, so the queue is one group under one header, which is row 0.
        group = ViewerSession.OpenMenu(state, 0);
        if (group.Menu is null)
        {
            throw new("No menu opened over the first row.");
        }
    }

    [Benchmark]
    public object AcceptAll() =>
        Done(ViewerSession.Apply(all, CommandKind.AcceptAll, applied));

    [Benchmark]
    public object AcceptGroup() =>
        Done(ViewerSession.Apply(group, CommandKind.AcceptGroup, applied));

    SessionState Done(SessionState state)
    {
        if (state.Queue.Count != 0)
        {
            throw new($"{state.Queue.Count} of {Entries} were left: {state.Message}");
        }

        return state;
    }
}
