/// <summary>
/// Pending moves and deletes in a viewer that owns them, which is what happens with no tray
/// running: DiffEngine addresses the queue owner instead of the piper port, and a delete even
/// starts this window, because it has no diff tool to open.
/// <para>
/// The attached case is the other half and behaves differently by design — there the entries
/// belong to the tray and every command is forwarded rather than applied, which
/// <c>TrayViewerSyncTest</c> covers.
/// </para>
/// </summary>
public class TrackedFileTests
{
    [Test]
    public async Task AcceptingADeleteRemovesTheFile()
    {
        var done = new List<string>();
        var state = Owned(Fixtures.Delete());

        var accepted = Act(state, CommandKind.Accept, Tracking(done));

        await Assert.That(done).IsEquivalentTo(["delete code/extra.verified.txt"]);
        await Assert.That(accepted.Queue).IsEmpty();
        await Assert.That(accepted.Message).IsEqualTo("Accepted extra.verified.txt");
    }

    /// <summary>
    /// Discarding a pending delete has always meant leaving the file and only untracking it. The
    /// next run re-tracks it.
    /// </summary>
    [Test]
    public async Task DiscardingADeleteLeavesTheFile()
    {
        var done = new List<string>();
        var state = Owned(Fixtures.Delete());

        var discarded = Act(state, CommandKind.Discard, Tracking(done));

        await Assert.That(done).IsEmpty();
        await Assert.That(discarded.Queue).IsEmpty();
        await Assert.That(discarded.Message).IsEqualTo("Discarded extra.verified.txt");
    }

    [Test]
    public async Task AcceptingAMoveMovesTheReceivedFileOverTheTarget()
    {
        var done = new List<string>();
        var state = Owned(Fixtures.Move());

        var accepted = Act(state, CommandKind.Accept, Tracking(done));

        await Assert.That(done).IsEquivalentTo(["move temp/sample.received.txt > code/sample.verified.txt"]);
        await Assert.That(accepted.Queue).IsEmpty();
    }

    [Test]
    public async Task DiscardingAMoveThrowsTheReceivedFileAway()
    {
        var done = new List<string>();
        var state = Owned(Fixtures.Move());

        var discarded = Act(state, CommandKind.Discard, Tracking(done));

        await Assert.That(done).IsEquivalentTo(["delete temp/sample.received.txt"]);
        await Assert.That(discarded.Queue).IsEmpty();
    }

    /// <summary>
    /// The one that matters most. Every inline command rebuilds its half of the queue from
    /// <see cref="InlineQueue"/>, and an owning viewer holds tracked files beside the snapshots, so
    /// accepting one snapshot must not take the pending files with it.
    /// </summary>
    [Test]
    public async Task AcceptingASnapshotKeepsTheFilesPendingBesideIt()
    {
        var state = Owned(Fixtures.Move(), Fixtures.Delete());
        state = ViewerSession.EnqueueInline(state, Fixtures.Patch());
        state = ViewerSession.SelectKey(state, QueueEntry.KeyForInline("SampleTests.cs", 42));

        var accepted = ViewerSession.Apply(state, CommandKind.Accept, Fixtures.Applied);

        await Assert.That(accepted.Queue.Select(_ => _.Kind))
            .IsEquivalentTo([QueueEntryKind.Move, QueueEntryKind.Delete]);
        await Assert.That(accepted.Exit).IsFalse();
    }

    [Test]
    public async Task SettlingASnapshotKeepsTheFilesPendingBesideIt()
    {
        var state = Owned(Fixtures.Delete());
        state = ViewerSession.EnqueueInline(state, Fixtures.Patch());

        var settled = ViewerSession.Settle(state, QueueEntry.KeyForInline("SampleTests.cs", 42));

        await Assert.That(settled.Queue.Single().Kind).IsEqualTo(QueueEntryKind.Delete);
    }

    /// <summary>
    /// Worded the way an owning tray words its own sweep, so the same click reads the same
    /// whichever process is holding the files.
    /// </summary>
    [Test]
    public async Task AcceptAllSweepsTheSnapshotsAndTheFiles()
    {
        var done = new List<string>();
        var state = Owned(Fixtures.Move(), Fixtures.Delete());
        state = ViewerSession.EnqueueInline(state, Fixtures.Patch());

        var accepted = ViewerSession.Apply(state, CommandKind.AcceptAll, Tracking(done));

        await Assert.That(done.Count).IsEqualTo(2);
        await Assert.That(accepted.Queue).IsEmpty();
        await Assert.That(accepted.Message).IsEqualTo("Accepted 1, plus 2 files");
    }

    [Test]
    public async Task DiscardAllSweepsTheSnapshotsAndTheFiles()
    {
        var done = new List<string>();
        var state = Owned(Fixtures.Move(), Fixtures.Delete());
        state = ViewerSession.EnqueueInline(state, Fixtures.Patch());

        var discarded = ViewerSession.Apply(state, CommandKind.DiscardAll, Tracking(done));

        // The move's received file goes, the pending delete's file stays.
        await Assert.That(done).IsEquivalentTo(["delete temp/sample.received.txt"]);
        await Assert.That(discarded.Queue).IsEmpty();
        await Assert.That(discarded.Message).IsEqualTo("Discarded 1, plus 2 files");
    }

    [Test]
    public async Task ASweepCountsWhatItCouldNotApply()
    {
        var state = Owned(Fixtures.Move(), Fixtures.Delete());

        var accepted = ViewerSession.Apply(state, CommandKind.AcceptAll, Failing("the file is locked"));

        await Assert.That(accepted.Message).IsEqualTo("Accepted 0, plus 0 files (2 kept)");
        await Assert.That(accepted.Queue.All(_ => _.Status == "the file is locked")).IsTrue();
    }

    /// <summary>
    /// Kept pending carrying the reason, so it can be retried once whatever holds the file is
    /// gone — the same bargain a failed inline apply makes.
    /// </summary>
    [Test]
    public async Task AFailedAcceptKeepsItsEntryAndSaysWhy()
    {
        var state = Owned(Fixtures.Delete());

        var accepted = Act(state, CommandKind.Accept, Failing("the file is locked"));

        await Assert.That(accepted.Queue.Single().Status).IsEqualTo("the file is locked");
        await Assert.That(accepted.Message).IsEqualTo("the file is locked");
        await Assert.That(accepted.Exit).IsFalse();
    }

    /// <summary>
    /// A re-run stages the same received file again, and a second entry for it would be a
    /// duplicate rather than news.
    /// </summary>
    [Test]
    public async Task TrackingTheSameFileAgainReplacesItsEntry()
    {
        var state = Owned(Fixtures.Move(left: "first"));

        var again = ViewerSession.EnqueueTracked(state, Fixtures.Move(left: "second"));

        await Assert.That(again.Queue).HasSingleItem();
        await Assert.That(again.Queue.Single().LeftText).IsEqualTo("second");
    }

    /// <summary>
    /// A solution header spans tracked files as well as snapshots, so its sweep has to as well:
    /// "Accept all in ..." must not quietly mean "accept the snapshots in ...".
    /// </summary>
    [Test]
    public async Task AGroupAcceptSweepsThatGroupsFiles()
    {
        var done = new List<string>();
        var state = Owned(
            Fixtures.Move(solution: "Alpha"),
            Fixtures.Delete(solution: "Beta"));

        var menu = ViewerSession.OpenMenu(state, 0);
        var accepted = ViewerSession.Apply(menu, CommandKind.AcceptGroup, Tracking(done));

        await Assert.That(done).IsEquivalentTo(["move temp/sample.received.txt > code/sample.verified.txt"]);
        await Assert.That(accepted.Queue.Single().Kind).IsEqualTo(QueueEntryKind.Delete);
    }

    /// <summary>
    /// A snapshot moving inline arrives as two unrelated entries — the patch that writes the
    /// literal, and a delete of the verified file it replaces — and the sweep used to run the
    /// second whether or not the first landed. That is how a snapshot leaves a repository
    /// altogether: nothing written into the source, nothing left beside it, and every later run
    /// reporting the same new snapshot with nothing to delete.
    /// </summary>
    [Test]
    public async Task ARefusedPatchHoldsTheDeleteThatWouldHaveReplacedIt()
    {
        var done = new List<string>();
        var state = Owned(Fixtures.Delete());
        state = ViewerSession.EnqueueInline(state, Fixtures.Patch());

        var accepted = ViewerSession.Apply(state, CommandKind.AcceptAll, Refusing(done, "no Verify call"));

        await Assert.That(done).IsEmpty();
        await Assert.That(accepted.Message).Contains("(1 kept)");
        var held = accepted.Queue.Single(_ => _.Kind == QueueEntryKind.Delete);
        await Assert.That(held.Status).Contains("Held");
    }

    /// <summary>
    /// Only the deletes wait. A move is the snapshot arriving rather than the last copy of it
    /// leaving, so holding those would strand accepted work for an unrelated failure.
    /// </summary>
    [Test]
    public async Task ARefusedPatchStillLetsAMoveThrough()
    {
        var done = new List<string>();
        var state = Owned(Fixtures.Move());
        state = ViewerSession.EnqueueInline(state, Fixtures.Patch());

        ViewerSession.Apply(state, CommandKind.AcceptAll, Refusing(done, "no Verify call"));

        await Assert.That(done).IsEquivalentTo(["move temp/sample.received.txt > code/sample.verified.txt"]);
    }

    /// <summary>
    /// The same guard on the group sweep. It reaches it differently: a group accept takes its
    /// entries one at a time, so a refused patch leaves the queue rather than staying in it with a
    /// status, and the sweep cannot read the outcome off the queue it is handed.
    /// </summary>
    [Test]
    public async Task ARefusedPatchHoldsTheDeleteInAGroupSweepToo()
    {
        var done = new List<string>();
        // Two solutions, so the headers are solution headers - the only kind that spans a snapshot
        // and a file together. A test header holds inline entries alone and sweeps nothing.
        var state = Owned(
            Fixtures.Delete(solution: "Alpha"),
            Fixtures.Delete(name: "other.verified.txt", solution: "Beta"));
        state = ViewerSession.EnqueueInline(
            state,
            Fixtures.Patch(Fixtures.SolutionFile("Alpha", "Tests", "ATests.cs")));

        var menu = ViewerSession.OpenMenu(state, 0);
        var accepted = ViewerSession.Apply(menu, CommandKind.AcceptGroup, Refusing(done, "no Verify call"));

        await Assert.That(done).IsEmpty();
        var held = accepted.Queue.Single(_ => _.Name == "extra.verified.txt");
        await Assert.That(held.Status).Contains("Held");
    }

    static SessionState Owned(params QueueEntry[] tracked)
    {
        var state = SessionState.Start(ViewerMode.Inline, Fixtures.Columns, Fixtures.Rows);
        foreach (var entry in tracked)
        {
            state = ViewerSession.EnqueueTracked(state, entry);
        }

        return state;
    }

    /// <summary>
    /// Acts on the first entry, which is what the window does with the selection it is showing.
    /// </summary>
    static SessionState Act(SessionState state, CommandKind command, ViewerActions actions) =>
        ViewerSession.Apply(ViewerSession.Apply(state, Command.Select(0)), command, actions);

    /// <summary>
    /// Records rather than performs, so accepting a pending file is reachable without staging one
    /// on disk — the same thing <see cref="Fixtures.Applying"/> does for patches.
    /// </summary>
    static ViewerActions Tracking(List<string> done) =>
        Fixtures.Applied with
        {
            MoveFile = (temp, target) => done.Add($"move {temp} > {target}"),
            DeleteFile = file => done.Add($"delete {file}")
        };

    /// <summary>
    /// The files still record rather than perform, but the patches are refused - an accept-all
    /// where the source could not take the literal.
    /// </summary>
    static ViewerActions Refusing(List<string> done, string reason) =>
        Tracking(done) with
        {
            ApplyInline = _ => InlineApplyResult.NotFound(reason)
        };

    static ViewerActions Failing(string message) =>
        Fixtures.Applied with
        {
            MoveFile = (_, _) => throw new IOException(message),
            DeleteFile = _ => throw new IOException(message)
        };
}
