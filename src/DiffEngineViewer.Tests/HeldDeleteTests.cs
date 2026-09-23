/// <summary>
/// When a sweep holds its deletes. A snapshot moving inline arrives as two unrelated entries - the
/// patch and a delete of the verified file it replaces - so a patch the applier will not take must
/// stop the delete, or the snapshot loses both copies at once.
/// </summary>
public class HeldDeleteTests
{
    /// <summary>
    /// A refusal recorded on a conflicted entry belongs to the targeted accept that made it, not
    /// to the batch reading it. A bulk accept hands conflicted entries back untouched, so that
    /// status sat there and held every pending delete on every later accept-all, citing a refusal
    /// that had not happened.
    /// </summary>
    [Test]
    public async Task A_refusal_left_on_a_conflicted_entry_does_not_hold_a_later_sweep()
    {
        var state = Conflicted();

        // One variant accepted on its own, and refused - the file was locked, say. The entry
        // stays, conflicted still, and carries what the applier said
        var refused = ViewerSession.Apply(
            state,
            CommandKind.Accept,
            Sweeping(InlineApplyResult.Failed("Failed to write: BTests.cs")));
        await Assert.That(refused.Queue.Single(_ => _.Kind == QueueEntryKind.Inline).Status).IsNotNull();

        var swept = ViewerSession.Apply(refused, CommandKind.AcceptAll, Sweeping(InlineApplyResult.Applied));
        await Assert.That(swept.Queue.Where(_ => _.Kind == QueueEntryKind.Delete)).IsEmpty();
    }

    /// <summary>
    /// The reason the read exists at all: a patch this batch could not write still holds the
    /// deletes.
    /// </summary>
    [Test]
    public async Task A_refusal_in_this_sweep_holds_the_deletes()
    {
        var state = ViewerSession.EnqueueTracked(
            Fixtures.Inline(Fixtures.Patch("A.cs", 1)),
            Fixtures.Delete());

        var swept = ViewerSession.Apply(
            state,
            CommandKind.AcceptAll,
            Sweeping(InlineApplyResult.NotFound("Could not locate the VerifyInline call")));

        var delete = swept.Queue.Single(_ => _.Kind == QueueEntryKind.Delete);
        await Assert.That(delete.Status).StartsWith("Held:");
    }

    /// <summary>
    /// A delete that is actually deletable, so that a held one and a failed one cannot be confused
    /// for each other: the fixture's default actions throw for anything touching a file.
    /// </summary>
    static ViewerActions Sweeping(InlineApplyResult result) =>
        Fixtures.Applying(result) with
        {
            DeleteFile = static _ =>
            {
            }
        };

    static SessionState Conflicted() =>
        ViewerSession.EnqueueTracked(
            Fixtures.Inline(
                Fixtures.Patch(content: "eight", framework: "net8.0"),
                Fixtures.Patch(content: "nine", framework: "net9.0")),
            Fixtures.Delete());

    /// <summary>
    /// B was accepted on its own earlier and refused, so it carries a status. "Accept all in
    /// SolutionA" then writes A's only snapshot, and carries out A's delete: whether a group holds
    /// its deletes is about what that group's own accepts did, not about statuses other accepts
    /// left elsewhere in the queue.
    /// </summary>
    [Test]
    public async Task An_earlier_failure_in_another_solution_does_not_hold_this_solutions_deletes()
    {
        var a = Fixtures.Patch(Fixtures.SolutionFile("SolutionA", "Tests", "ATests.cs"), 10);
        var b = Fixtures.Patch(Fixtures.SolutionFile("SolutionB", "Tests", "BTests.cs"), 10);
        var state = ViewerSession.EnqueueTracked(Fixtures.Inline(a, b), Fixtures.Delete(solution: "SolutionA"));
        state = ViewerSession.SelectKey(state, QueueEntry.KeyForInline(b.SourceFile, b.LineHint));
        state = ViewerSession.Apply(
            state,
            CommandKind.Accept,
            Fixtures.Applying(InlineApplyResult.Failed("Failed to write: BTests.cs")));
        await Assert.That(state.Queue.Single(_ => _.Key == QueueEntry.KeyForInline(b.SourceFile, b.LineHint)).Status).IsNotNull();

        var deleted = new List<string>();
        var actions = Fixtures.Applied with
        {
            DeleteFile = _ => deleted.Add(_)
        };
        var visible = QueueProjection.Visible(state, ScreenBuilder.BodyRows(state), out _).ToList();
        state = ViewerSession.OpenMenu(state, visible.FindIndex(_ => _.GroupName == "SolutionA"));
        await Assert.That(state.Menu!.Items[1].Label).IsEqualTo("Accept all in SolutionA");

        var swept = ViewerSession.Apply(state, CommandKind.AcceptGroup, actions);

        await Assert.That(deleted).IsEquivalentTo(["code/extra.verified.txt"]);
        await Assert.That(swept.Queue.Where(_ => _.Kind == QueueEntryKind.Delete)).IsEmpty();
    }

    /// <summary>
    /// And one of the group's own that failed, rather than going stale, holds them: it is still in
    /// the queue, unwritten.
    /// </summary>
    [Test]
    public async Task A_failure_in_this_solution_holds_its_deletes()
    {
        var a = Fixtures.Patch(Fixtures.SolutionFile("SolutionA", "Tests", "ATests.cs"), 10);
        var b = Fixtures.Patch(Fixtures.SolutionFile("SolutionB", "Tests", "BTests.cs"), 10);
        var state = ViewerSession.EnqueueTracked(Fixtures.Inline(a, b), Fixtures.Delete(solution: "SolutionA"));
        var deleted = new List<string>();
        var actions = Fixtures.Applying(InlineApplyResult.Failed("Failed to write: ATests.cs")) with
        {
            DeleteFile = _ => deleted.Add(_)
        };
        var visible = QueueProjection.Visible(state, ScreenBuilder.BodyRows(state), out _).ToList();
        state = ViewerSession.OpenMenu(state, visible.FindIndex(_ => _.GroupName == "SolutionA"));

        var swept = ViewerSession.Apply(state, CommandKind.AcceptGroup, actions);

        await Assert.That(deleted).IsEmpty();
        await Assert.That(swept.Queue.Single(_ => _.Kind == QueueEntryKind.Delete).Status).IsNotNull();
    }
}
