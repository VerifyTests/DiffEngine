/// <summary>
/// "Accept all in" a header is an accept-all over that header's members: the same batch, an entry
/// at a time and outside the lock, with fewer entries in it. It used to be one transition that
/// applied the whole group before it returned, which in a queue of one solution is the whole
/// queue, on the render thread.
/// </summary>
public class GroupAcceptBatchTests
{
    /// <summary>
    /// What the window does with the click: begins the batch, and writes nothing. Real actions are
    /// what a click is dispatched with, so anything applied here would be an attempt on a source
    /// file that does not exist, and would show as a failed entry.
    /// </summary>
    [Test]
    public async Task The_click_begins_a_batch_and_applies_nothing()
    {
        var open = OverSolutionA(TwoSolutions());

        var clicked = ViewerProgram.Apply(open, Click(open, "Accept all in SolutionA"), link: null, new NoWindow());

        await Assert.That(clicked.Batch).IsNotNull();
        await Assert.That(clicked.Batch!.Total).IsEqualTo(2);
        await Assert.That(clicked.Batch.Current).IsNull();
        await Assert.That(clicked.Queue.Count).IsEqualTo(3);
        await Assert.That(clicked.Queue.All(_ => _.Status is null)).IsTrue();
        await Assert.That(clicked.Menu).IsNull();
        await Assert.That(ScreenBuilder.Build(clicked).Status).IsEqualTo("Accepting 1 of 2");
    }

    /// <summary>
    /// Carried out the way the loop has it carried out, the lock is free while each member is
    /// applied, and the batch reaches the group's members and nobody else's.
    /// </summary>
    [Test]
    public async Task The_batch_is_applied_outside_the_lock_and_stops_at_the_group()
    {
        var open = OverSolutionA(TwoSolutions());
        var host = new SessionHost(open);
        var seen = new List<string>();
        var actions = Fixtures.Applied with
        {
            ApplyInline = _ =>
            {
                // Another thread, standing in for the render loop, since the lock lets the thread
                // that holds it back in
                var drawn = Task.Run(() => host.Mutate(state => state));
                if (!drawn.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new("The lock was held across an apply.");
                }

                seen.Add($"{ScreenBuilder.Build(drawn.Result).Status}: {Path.GetFileName(_.SourceFile)}");
                return InlineApplyResult.Applied;
            }
        };
        host.Mutate(ViewerSession.BeginAcceptGroup);

        var message = new AcceptAllRunner(host, actions).Drive();

        await Assert.That(string.Join("\n", seen)).IsEqualTo(
            """
            Accepting 1 of 2: ATests.cs
            Accepting 2 of 2: OtherTests.cs
            """);
        await Assert.That(message).IsEqualTo("Accepted 2");
        await Assert.That(host.State.Queue.Select(_ => _.Name)).IsEquivalentTo(["BTests.cs:3"]);
        await Assert.That(host.State.Batch).IsNull();
    }

    /// <summary>
    /// What still needs review is counted over the group. A conflict in another solution is not
    /// this accept's to report, as it was not before.
    /// </summary>
    [Test]
    public async Task A_conflict_outside_the_group_is_not_counted()
    {
        var state = ViewerSession.EnqueueInline(
            TwoSolutions(),
            Fixtures.Patch(Fixtures.SolutionFile("SolutionB", "Tests", "BTests.cs"), 3, "\"x\"", "z", framework: "net9.0"));
        await Assert.That(state.Queue.Single(_ => _.Name == "BTests.cs:3").Conflicted).IsTrue();

        var accepted = ViewerSession.Apply(OverSolutionA(state), CommandKind.AcceptGroup, Fixtures.Applied);

        await Assert.That(accepted.Message).IsEqualTo("Accepted 2");
    }

    /// <summary>
    /// And one inside it is, and is left for a reviewer.
    /// </summary>
    [Test]
    public async Task A_conflict_inside_the_group_is_counted_and_kept()
    {
        var state = ViewerSession.EnqueueInline(
            TwoSolutions(),
            Fixtures.Patch(Fixtures.SolutionFile("SolutionA", "Tests", "OtherTests.cs"), 2, "\"a\"", "c", framework: "net9.0"));

        var accepted = ViewerSession.Apply(OverSolutionA(state), CommandKind.AcceptGroup, Fixtures.Applied);

        await Assert.That(accepted.Message).IsEqualTo("Accepted 1, 1 conflict needs review");
        await Assert.That(accepted.Queue.Select(_ => _.Name)).IsEquivalentTo(["OtherTests.cs:2", "BTests.cs:3"]);
    }

    /// <summary>
    /// The batch's rule for a snapshot the applier would not take: it stays, saying why. A group
    /// accept used to drop it as a single accept does, which out of a group of thirty is an entry
    /// nobody saw go, with no literal written and nothing left in the queue to say so.
    /// </summary>
    [Test]
    public async Task A_snapshot_that_was_not_written_stays_and_says_why()
    {
        var actions = Fixtures.Applying(InlineApplyResult.NotFound("no Verify call"));

        var accepted = ViewerSession.Apply(OverSolutionA(TwoSolutions()), CommandKind.AcceptGroup, actions);

        await Assert.That(accepted.Queue.Count).IsEqualTo(3);
        var members = accepted.Queue.Where(_ => _.Name != "BTests.cs:3").ToList();
        await Assert.That(members.All(_ => _.Status!.Contains("no Verify call"))).IsTrue();
        await Assert.That(accepted.Queue.Single(_ => _.Name == "BTests.cs:3").Status).IsNull();
        await Assert.That(accepted.Message).IsEqualTo("Accepted 0, 2 not written");
    }

    /// <summary>
    /// Nothing else that changes the queue is taken while a group's batch runs, the same as while
    /// an accept-all does: the two are one mechanism, and a second one begun over the first would
    /// claim entries the first is part way through.
    /// </summary>
    [Test]
    public async Task A_second_accept_is_refused_while_a_groups_batch_runs()
    {
        var running = ViewerSession.ClaimNext(ViewerSession.BeginAcceptGroup(OverSolutionA(TwoSolutions())));
        await Assert.That(running.Batch!.Current).IsNotNull();

        var again = ViewerProgram.Apply(running, Key(CommandKind.AcceptAll), link: null, new NoWindow());

        await Assert.That(again.Batch).IsSameReferenceAs(running.Batch);
        await Assert.That(ViewerSession.BeginAcceptAll(running)).IsSameReferenceAs(running);
    }

    /// <summary>
    /// A header with no menu open over it has no group to accept, and the state is left alone.
    /// </summary>
    [Test]
    public async Task With_no_menu_open_there_is_no_group_to_accept()
    {
        var state = TwoSolutions();

        await Assert.That(ViewerSession.BeginAcceptGroup(state)).IsSameReferenceAs(state);
    }

    // Each from one framework, so the same call site reported by another with other content is a
    // conflict rather than a re-run.
    static SessionState TwoSolutions() =>
        Fixtures.Inline(
            Fixtures.Patch(Fixtures.SolutionFile("SolutionA", "Tests", "ATests.cs"), 1, framework: "net8.0"),
            Fixtures.Patch(Fixtures.SolutionFile("SolutionA", "Tests", "OtherTests.cs"), 2, "\"a\"", "b", framework: "net8.0"),
            Fixtures.Patch(Fixtures.SolutionFile("SolutionB", "Tests", "BTests.cs"), 3, "\"x\"", "y", framework: "net8.0"));

    static SessionState OverSolutionA(SessionState state)
    {
        var rows = QueueProjection.Visible(state, ScreenBuilder.BodyRows(state), out _).ToList();
        return ViewerSession.OpenMenu(state, rows.FindIndex(_ => _.GroupName == "SolutionA"));
    }

    static ViewerInput Click(SessionState state, string label) =>
        Key(CommandKind.None) with
        {
            ClickedMenuItem = state.Menu!.Items.ToList().FindIndex(_ => _.Label == label)
        };

    static ViewerInput Key(CommandKind key) =>
        new(key, -1, -1, 0, false, Fixtures.Columns, Fixtures.Rows);

    sealed class NoWindow : IViewerWindow
    {
        public bool Present(Screen screen) =>
            true;

        public ViewerInput Poll() =>
            default;

        public void SetHidden(bool hidden)
        {
        }

        public void Focus()
        {
        }

        public void SetClipboard(string text)
        {
        }

        public bool Capture(Screen screen, int width, int height, string pngPath) =>
            false;

        public void Dispose()
        {
        }
    }
}
