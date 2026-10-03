/// <summary>
/// The queue column is sliced to the rows that fit before any of them is described. Every frame
/// of a scroll or a drag is another state and so another screen, and describing every row of the
/// queue to draw the forty that fit was half a millisecond and a megabyte a frame at 2,000
/// entries. A row has to say what it said when every row was described.
/// </summary>
public class QueueSliceTests
{
    /// <summary>
    /// Whichever entry is selected and whatever is folded, the rows on screen are the same rows of
    /// the whole list, labels grown by a collision elsewhere in the queue included.
    /// </summary>
    [Test]
    public async Task TheRowsOnScreenAreRowsOfTheWholeList()
    {
        var random = new Random(21);
        for (var iteration = 0; iteration < 40; iteration++)
        {
            // Ten rows of window is two of queue, so nearly every queue here is longer than it
            var state = ViewerSession.Resize(Mixed(random), Fixtures.Columns, 10 + random.Next(6));
            await EverySelection(state);
            foreach (var header in QueueProjection.Rows(state).Where(_ => _.GroupKey is not null).ToList())
            {
                await EverySelection(ViewerSession.ToggleGroup(state, header.GroupKey!));
            }
        }
    }

    static async Task EverySelection(SessionState state)
    {
        var body = ScreenBuilder.BodyRows(state);
        for (var selected = 0; selected < state.Queue.Count; selected++)
        {
            var selecting = state with { Selected = selected };
            var rows = QueueProjection.Rows(selecting);
            var visible = QueueProjection.Visible(selecting, body, out var top);
            await Assert.That(visible.Count).IsEqualTo(Math.Min(body, rows.Count));
            await Assert.That(visible.SequenceEqual(rows.Skip(top).Take(body))).IsTrue();
        }
    }

    /// <summary>
    /// A label is grown to tell an entry from one it would otherwise read the same as, and the
    /// other one need not be on screen. So the labels are decided over the whole queue, whichever
    /// rows are described.
    /// </summary>
    [Test]
    public async Task ALabelIsGrownByAnEntryThatIsNotOnScreen()
    {
        var patches = new List<InlinePatch>
        {
            Fixtures.Patch(Fixtures.SolutionFile("SolutionA", "First", "Tests.cs"), 10, testName: "Same name")
        };
        for (var index = 0; index < 30; index++)
        {
            patches.Add(Fixtures.Patch(Fixtures.SolutionFile("SolutionA", "First", $"Other{index}.cs"), 10, testName: $"Test {index}"));
        }

        patches.Add(Fixtures.Patch(Fixtures.SolutionFile("SolutionA", "Second", "Tests.cs"), 10, testName: "Same name"));
        var state = Fixtures.Inline([.. patches]);

        var visible = QueueProjection.Visible(state, ScreenBuilder.BodyRows(state), out var top);

        await Assert.That(top).IsEqualTo(0);
        await Assert.That(visible.Count).IsLessThan(state.Queue.Count);
        await Assert.That(visible[0].Label).IsEqualTo("First/Same name");
    }

    /// <summary>
    /// What the slicing is for, seen by what a screen allocates: a scroll leaves the queue the
    /// list it was, and the screen built after one describes the rows that fit and no others.
    /// </summary>
    [Test]
    public async Task AScrollDescribesOnlyTheRowsThatFit()
    {
        var state = SessionState.Start(ViewerMode.Inline, Fixtures.Columns, Fixtures.Rows);
        for (var index = 0; index < 2000; index++)
        {
            state = ViewerSession.EnqueueInline(
                state,
                Fixtures.Patch($"Sample{index % 40}Tests.cs", 10 + index, "\"old\"", $"new {index}\nand more\nand more", testName: $"Test{index}"));
        }

        // The first screen of a queue works out its labels, which is once a queue
        ScreenBuilder.Build(state);
        var scrolled = ViewerSession.Apply(state, CommandKind.ScrollDown);
        await Assert.That(scrolled).IsNotSameReferenceAs(state);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var screen = ScreenBuilder.Build(scrolled);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(screen.Queue.Count).IsEqualTo(ScreenBuilder.BodyRows(state));
        // Every row described was over a megabyte. What is left is the walk, a few bytes an entry
        await Assert.That(allocated).IsLessThan(150_000);
    }

    /// <summary>
    /// Snapshots across two solutions and outside any, some sharing a test and some sharing only
    /// a test's name across projects, so there are headers of both kinds and labels that collide.
    /// </summary>
    static SessionState Mixed(Random random)
    {
        string[] solutions = ["SolutionA", "SolutionB"];
        string[] projects = ["First", "Second"];
        var patches = new List<InlinePatch>();
        for (var index = random.Next(6, 24); index > 0; index--)
        {
            var file = $"Tests{random.Next(3)}.cs";
            var source = random.Next(4) == 0
                ? file
                : Fixtures.SolutionFile(solutions[random.Next(2)], projects[random.Next(2)], file);
            patches.Add(
                Fixtures.Patch(
                    source,
                    10 + patches.Count,
                    $"\"old{patches.Count}\"",
                    $"new {patches.Count}",
                    testName: random.Next(3) == 0 ? null : $"Test{random.Next(4)}"));
        }

        var state = Fixtures.Inline([.. patches]);
        state = ViewerSession.EnqueueTracked(state, Fixtures.Move(solution: "SolutionA"));
        return ViewerSession.EnqueueTracked(state, Fixtures.Delete(solution: "SolutionB"));
    }
}
