/// <summary>
/// A pending delete and a pending move that name the same verified file.
/// <para>
/// The delete says no test produces the file any more, and the move says a later run verified
/// against it and failed, so the delete is the stale one. Nothing withdrew it, and "Accept all"
/// carries out the moves and then the deletes: the received file was moved into place and then
/// deleted, and both were gone with nothing said.
/// </para>
/// <para>
/// DiffRunner.SettleDelete withdraws such a delete, which is why this takes an arrangement to
/// reach: a library from before that existed, a viewer holding the queue the settle is sent to, or
/// a settle skipped because the port was remembered as unowned.
/// </para>
/// </summary>
public class TrackerMoveOntoDeleteTest :
    IDisposable
{
    [Test]
    public async Task AMoveOntoAFileWithdrawsItsPendingDelete()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(verified);

        tracker.AddMove(received, verified, "theExe", "theArguments", true, null);

        await Assert.That(tracker.Deletes).IsEmpty();
        await Assert.That(tracker.Moves).HasSingleItem();
    }

    /// <summary>
    /// The same pair arriving over the viewer port, which is how a move reaches a tray that started
    /// after the test process did.
    /// </summary>
    [Test]
    public async Task AMoveArrivingOverTheViewerPortWithdrawsItToo()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(verified);

        ((ITrackedFiles) tracker).AddMove(received, verified);

        await Assert.That(tracker.Deletes).IsEmpty();
    }

    [Test]
    public async Task AcceptAllKeepsTheFileItsMoveJustWrote()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(verified);
        tracker.AddMove(received, verified, "theExe", "theArguments", true, null);

        await tracker.AcceptAll();

        await Assert.That(await File.ReadAllTextAsync(verified)).IsEqualTo("received");
        await Assert.That(File.Exists(received)).IsFalse();
        await tracker.AssertEmpty();
    }

    /// <summary>
    /// The other order, which nothing withdraws: the delete arrived after the move, so it may be
    /// the newer of the two. Neither is guessed at. The move is what was asked for, and a delete is
    /// never carried out on a file the same sweep has just written, so it stays pending for
    /// whoever knows the file is redundant.
    /// </summary>
    [Test]
    public async Task AcceptAllHoldsADeleteOfTheFileItsMoveJustWrote()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddMove(received, verified, "theExe", "theArguments", true, null);
        tracker.AddDelete(verified);

        await tracker.AcceptAll();

        await Assert.That(await File.ReadAllTextAsync(verified)).IsEqualTo("received");
        await Assert.That(tracker.Moves).IsEmpty();
        await Assert.That(tracker.Deletes.Select(_ => _.File)).IsEquivalentTo([verified]);
    }

    /// <summary>
    /// The sweep that held the delete forgot why as it ended. The delete sat in the menu looking
    /// like any other, and pressing "Accept all" again, which is what a delete still listed after
    /// an accept-all invites, deleted the snapshot the first press had just accepted.
    /// </summary>
    [Test]
    public async Task ASecondAcceptAllStillHoldsTheDelete()
    {
        var warnings = new ConcurrentQueue<string>();
        await using var tracker = new RecordingTracker(inlineFailed: warnings.Enqueue);
        tracker.AddMove(received, verified, "theExe", "theArguments", true, null);
        var delete = tracker.AddDelete(verified);
        await tracker.AcceptAll();

        await tracker.AcceptAll();

        await Assert.That(await File.ReadAllTextAsync(verified)).IsEqualTo("received");
        await Assert.That(tracker.Deletes.Select(_ => _.File)).IsEquivalentTo([verified]);
        await Assert.That(tracker.HeldReason(delete)).IsEqualTo(Tracker.WroteItsFile);
        // Said each time, since each press was an accept-all that left something undone
        await Assert.That(warnings).IsEquivalentTo(
        [
            Tracker.DeletesKept([delete]),
            Tracker.DeletesKept([delete])
        ]);
    }

    /// <summary>
    /// The same loss with no sweep in the first half of it: the move accepted from its own menu
    /// item, and then "Accept all" for what was left.
    /// </summary>
    [Test]
    public async Task AMoveAcceptedOnItsOwnHoldsTheDeleteFromALaterAcceptAll()
    {
        await using var tracker = new RecordingTracker();
        var move = tracker.AddMove(received, verified, "theExe", "theArguments", true, null);
        tracker.AddDelete(verified);
        tracker.Accept(move);

        await tracker.AcceptAll();

        await Assert.That(await File.ReadAllTextAsync(verified)).IsEqualTo("received");
        await Assert.That(tracker.Deletes).HasSingleItem();
    }

    [Test]
    public async Task ASecondWireSweepStillHoldsTheDelete()
    {
        await using var tracker = new RecordingTracker();
        ITrackedFiles tracked = tracker;
        tracker.AddMove(received, verified, "theExe", "theArguments", true, null);
        tracker.AddDelete(verified);
        tracked.AcceptAllTracked(holdDeletes: false);

        var (accepted, kept) = tracked.AcceptAllTracked(holdDeletes: false);

        await Assert.That(accepted).IsEqualTo(0);
        await Assert.That(kept).IsEqualTo(1);
        await Assert.That(await File.ReadAllTextAsync(verified)).IsEqualTo("received");
    }

    /// <summary>
    /// Held from a sweep, and no more than that. Whoever knows the file is redundant accepts the
    /// delete itself, from its own item or from the header over the deletes, or over the wire by
    /// its key.
    /// </summary>
    [Test]
    public async Task AHeldDeleteIsCarriedOutWhenAcceptedOnItsOwn()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddMove(received, verified, "theExe", "theArguments", true, null);
        var delete = tracker.AddDelete(verified);
        await tracker.AcceptAll();

        tracker.Accept(delete);

        await Assert.That(File.Exists(verified)).IsFalse();
        await tracker.AssertEmpty();
    }

    /// <summary>
    /// A test run that raises the delete again has looked at the file the move wrote and still
    /// says nothing produces it. That is the later statement, and the next sweep carries it out.
    /// </summary>
    [Test]
    public async Task ADeleteRaisedAgainAfterTheWriteIsCarriedOut()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddMove(received, verified, "theExe", "theArguments", true, null);
        var delete = tracker.AddDelete(verified);
        await tracker.AcceptAll();

        tracker.AddDelete(verified);
        await Assert.That(tracker.HeldReason(delete)).IsNull();
        await tracker.AcceptAll();

        await Assert.That(File.Exists(verified)).IsFalse();
        await tracker.AssertEmpty();
    }

    /// <summary>
    /// A delete waiting on a move that is still pending is held for as long as the move is, and
    /// no longer: discarded, the move is never going to write the file.
    /// </summary>
    [Test]
    public async Task ADeleteWaitingOnAMoveIsCarriedOutOnceTheMoveIsDiscarded()
    {
        await using var tracker = new RecordingTracker();
        var move = tracker.AddMove(received, verified, "theExe", "theArguments", true, null);
        var delete = tracker.AddDelete(verified);
        await Assert.That(tracker.HeldReason(delete)).IsEqualTo(Tracker.AwaitsItsFile);

        tracker.Discard(move);
        await Assert.That(tracker.HeldReason(delete)).IsNull();
        await tracker.AcceptAll();

        await Assert.That(File.Exists(verified)).IsFalse();
        await tracker.AssertEmpty();
    }

    /// <summary>
    /// Where the reason is read by somebody who was not looking when the balloon went by: beside
    /// the delete in the debug view, as it is in the menu.
    /// </summary>
    [Test]
    public async Task TheDebugViewSaysWhyADeleteIsHeld()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddMove(received, verified, "theExe", "theArguments", true, null);
        tracker.AddDelete(verified);
        await Assert.That(DebugReport.Build(tracker, DateTime.Now)).DoesNotContain(Tracker.WroteItsFile);

        await tracker.AcceptAll();

        await Assert.That(DebugReport.Build(tracker, DateTime.Now)).Contains(Tracker.WroteItsFile);
    }

    /// <summary>
    /// The sweep a viewer displaying the tray's queue asks for, which has the same two halves in
    /// the same order.
    /// </summary>
    [Test]
    public async Task TheWireSweepHoldsADeleteOfTheFileItsMoveJustWrote()
    {
        await using var tracker = new RecordingTracker();
        ITrackedFiles tracked = tracker;
        tracker.AddMove(received, verified, "theExe", "theArguments", true, null);
        tracker.AddDelete(verified);

        var (accepted, kept) = tracked.AcceptAllTracked(holdDeletes: false);

        await Assert.That(accepted).IsEqualTo(1);
        await Assert.That(kept).IsEqualTo(1);
        await Assert.That(await File.ReadAllTextAsync(verified)).IsEqualTo("received");
        await Assert.That(tracker.Deletes.Select(_ => _.File)).IsEquivalentTo([verified]);
    }

    /// <summary>
    /// A move that could not be carried out is still going to write the file, so the delete waits
    /// for it as well. Deleting here left the old snapshot gone and the new one not yet in place.
    /// </summary>
    [Test]
    public async Task TheWireSweepHoldsADeleteOfAFileAMoveStillPendingTargets()
    {
        await using var tracker = new RecordingTracker();
        ITrackedFiles tracked = tracker;
        tracker.AddMove(received, verified, "theExe", "theArguments", true, null);
        tracker.AddDelete(verified);

        int accepted;
        int kept;
        // The received file held open, so the move fails and stays pending while nothing stops
        // the verified file from being deleted
        await using (new FileStream(received, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            (accepted, kept) = tracked.AcceptAllTracked(holdDeletes: false);
        }

        await Assert.That(accepted).IsEqualTo(0);
        await Assert.That(kept).IsEqualTo(2);
        await Assert.That(await File.ReadAllTextAsync(verified)).IsEqualTo("verified");
        await Assert.That(tracker.Moves).HasSingleItem();
        await Assert.That(tracker.Deletes).HasSingleItem();
    }

    /// <summary>
    /// What holds a delete is a move that wrote the file or is still going to, not a move having
    /// named it. One whose received file has gone is dropped without writing anything - the test
    /// re-ran and cleared it - and the delete beside it is the newer statement about that file.
    /// </summary>
    [Test]
    public async Task AMoveWithNothingLeftToMoveDoesNotHoldTheDelete()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddMove(received, verified, "theExe", "theArguments", true, null);
        tracker.AddDelete(verified);
        File.Delete(received);

        await tracker.AcceptAll();

        await Assert.That(File.Exists(verified)).IsFalse();
        await tracker.AssertEmpty();
    }

    public TrackerMoveOntoDeleteTest()
    {
        // The received file sits in its own directory, the way DiffEngine stages one, because
        // accepting a move deletes that directory.
        directory = Path.Combine(Path.GetTempPath(), $"MoveOntoDelete_{Guid.NewGuid():N}");
        var staged = Path.Combine(directory, "staged");
        Directory.CreateDirectory(staged);
        received = Path.Combine(staged, "Sample.Test.received.txt");
        verified = Path.Combine(directory, "Sample.Test.verified.txt");
        File.WriteAllText(received, "received");
        File.WriteAllText(verified, "verified");
    }

    public void Dispose() =>
        Directory.Delete(directory, true);

    string directory;
    string received;
    string verified;
}
