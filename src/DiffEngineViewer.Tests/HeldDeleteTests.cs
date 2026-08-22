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
}
