/// <summary>
/// The tracker answering the wire for its moves and deletes: keyed by prefix, never prompting,
/// and never deleting on discard.
/// </summary>
public class TrackerTrackedFilesTest :
    IDisposable
{
    [Test]
    public async Task DeletesAndMovesListWithTheirKeys()
    {
        await using var tracker = new RecordingTracker();
        ITrackedFiles tracked = tracker;
        tracker.AddDelete(file);
        tracker.AddMove(temp, target, null, null, false, null);

        var delete = tracked.Deletes().Single();
        await Assert.That(delete.Key).IsEqualTo(TrackedKeys.ForDelete(file));
        await Assert.That(delete.File).IsEqualTo(file);

        var move = tracked.Moves().Single();
        await Assert.That(move.Key).IsEqualTo(TrackedKeys.ForMove(temp));
        await Assert.That(move.Temp).IsEqualTo(temp);
        await Assert.That(move.Target).IsEqualTo(target);

        await Assert.That(tracked.Has(delete.Key)).IsTrue();
        await Assert.That(tracked.Has(move.Key)).IsTrue();
        await Assert.That(tracked.Has(TrackedKeys.ForDelete("nothing"))).IsFalse();
        await Assert.That(tracked.Has("not a tracked key")).IsFalse();
    }

    [Test]
    public async Task AcceptingATrackedDeleteRemovesTheFile()
    {
        await using var tracker = new RecordingTracker();
        ITrackedFiles tracked = tracker;
        tracker.AddDelete(file);

        var (ok, message) = tracked.Accept(TrackedKeys.ForDelete(file));

        await Assert.That(ok).IsTrue();
        await Assert.That(message).IsEqualTo($"Deleted {Path.GetFileName(file)}");
        await Assert.That(File.Exists(file)).IsFalse();
        await Assert.That(tracker.Deletes).IsEmpty();
    }

    /// <summary>
    /// Discarding a delete has always meant leaving the file: Clear never touched disk, and the
    /// wire discard matches it. The next test run re-tracks it.
    /// </summary>
    [Test]
    public async Task DiscardingATrackedDeleteKeepsTheFileOnDisk()
    {
        await using var tracker = new RecordingTracker();
        ITrackedFiles tracked = tracker;
        tracker.AddDelete(file);

        var (ok, _) = tracked.Discard(TrackedKeys.ForDelete(file));

        await Assert.That(ok).IsTrue();
        await Assert.That(File.Exists(file)).IsTrue();
        await Assert.That(tracker.Deletes).IsEmpty();
    }

    [Test]
    public async Task AcceptingATrackedMoveMovesTheFile()
    {
        await using var tracker = new RecordingTracker();
        ITrackedFiles tracked = tracker;
        await File.WriteAllTextAsync(temp, "content");
        tracker.AddMove(temp, target, null, null, false, null);

        var (ok, _) = tracked.Accept(TrackedKeys.ForMove(temp));

        await Assert.That(ok).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(target)).IsEqualTo("content");
        await Assert.That(tracker.Moves).IsEmpty();
    }

    /// <summary>
    /// Wire accepts run on a listener thread with nobody at a dialog, so a locked move is refused
    /// with a pointer at the tray menu and the resolver is never consulted.
    /// </summary>
    [Test]
    public async Task ALockedMoveIsRefusedWithoutPrompting()
    {
        await using var tracker = new RecordingTracker(
            lockedFilesResolver: (_, _) => throw new("must not prompt"));
        ITrackedFiles tracked = tracker;
        await File.WriteAllTextAsync(temp, "content");
        tracker.AddMove(temp, target, null, null, false, null);
        // ReSharper disable once UseAwaitUsing
        using (new FileStream(target, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            var (ok, message) = tracked.Accept(TrackedKeys.ForMove(temp));

            await Assert.That(ok).IsFalse();
            await Assert.That(message).Contains("locked");
            await Assert.That(message).Contains("tray menu");
            await Assert.That(tracker.Moves).HasSingleItem();
        }
    }

    /// <summary>
    /// Settling a pair whose test started passing. Neither accept nor discard: DiffEngine has
    /// already taken the received file away, and both of those would act on disk.
    /// </summary>
    [Test]
    public async Task UntrackingLeavesBothFilesAlone()
    {
        await using var tracker = new RecordingTracker();
        ITrackedFiles tracked = tracker;
        tracker.AddDelete(file);
        await File.WriteAllTextAsync(temp, "content");
        await File.WriteAllTextAsync(target, "verified");
        tracker.AddMove(temp, target, null, null, false, null);

        await Assert.That(tracked.Untrack(TrackedKeys.ForMove(temp))).IsTrue();
        await Assert.That(tracked.Untrack(TrackedKeys.ForDelete(file))).IsTrue();

        await Assert.That(tracker.Moves).IsEmpty();
        await Assert.That(tracker.Deletes).IsEmpty();
        await Assert.That(File.Exists(temp)).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(target)).IsEqualTo("verified");
        await Assert.That(File.Exists(file)).IsTrue();
    }

    [Test]
    public async Task UntrackingSomethingUntrackedSaysSo()
    {
        await using var tracker = new RecordingTracker();
        ITrackedFiles tracked = tracker;

        await Assert.That(tracked.Untrack(TrackedKeys.ForMove("nothing"))).IsFalse();
        await Assert.That(tracked.Untrack("not a tracked key")).IsFalse();
    }

    [Test]
    public async Task AnUnknownTrackedKeyIsUnknown()
    {
        await using var tracker = new RecordingTracker();
        ITrackedFiles tracked = tracker;

        await Assert.That(tracked.Accept(TrackedKeys.ForMove("nothing"))).IsEqualTo((false, null));
        await Assert.That(tracked.Discard(TrackedKeys.ForDelete("nothing"))).IsEqualTo((false, null));
    }

    [Test]
    public async Task AcceptAllSweepsAndCountsWhatStayed()
    {
        await using var tracker = new RecordingTracker();
        ITrackedFiles tracked = tracker;
        tracker.AddDelete(file);
        await File.WriteAllTextAsync(temp, "content");
        tracker.AddMove(temp, target, null, null, false, null);

        var (accepted, kept) = tracked.AcceptAllTracked(holdDeletes: false);

        await Assert.That(accepted).IsEqualTo(2);
        await Assert.That(kept).IsEqualTo(0);
        await Assert.That(File.Exists(file)).IsFalse();
        await Assert.That(await File.ReadAllTextAsync(target)).IsEqualTo("content");
    }

    /// <summary>
    /// The owner lists the deletes as its batch begins and hands them back once its snapshots are
    /// done. One that arrived in between is not in the batch: it belongs to a patch the batch never
    /// had, so it is left pending and counted as neither accepted nor kept.
    /// </summary>
    [Test]
    public async Task AcceptAllCarriesOutOnlyTheDeletesListedWhenTheBatchBegan()
    {
        await using var tracker = new RecordingTracker();
        ITrackedFiles tracked = tracker;
        tracker.AddDelete(file);
        var listed = tracked
            .Deletes()
            .Select(_ => _.Key)
            .ToList();
        tracker.AddDelete(lateFile);

        var (accepted, kept) = tracked.AcceptAll(listed, holdDeletes: false);

        await Assert.That(accepted).IsEqualTo(1);
        await Assert.That(kept).IsEqualTo(0);
        await Assert.That(File.Exists(file)).IsFalse();
        await Assert.That(File.Exists(lateFile)).IsTrue();
        await Assert.That(tracker.Deletes.Select(_ => _.File)).IsEquivalentTo([lateFile]);
    }

    /// <summary>
    /// A delete that was listed and then settled while the snapshots were applying. There is
    /// nothing left to carry out, and the progress it was counted into still moves past it.
    /// </summary>
    [Test]
    public async Task AcceptAllPassesOverADeleteThatWentWhileTheBatchRan()
    {
        await using var tracker = new RecordingTracker();
        ITrackedFiles tracked = tracker;
        tracker.AddDelete(file);
        var listed = tracked
            .Deletes()
            .Select(_ => _.Key)
            .ToList();
        tracked.Untrack(TrackedKeys.ForDelete(file));
        var steps = 0;

        var (accepted, kept) = tracked.AcceptAll(listed, holdDeletes: false, () => steps++);

        await Assert.That(accepted).IsEqualTo(0);
        await Assert.That(kept).IsEqualTo(0);
        await Assert.That(steps).IsEqualTo(1);
        await Assert.That(File.Exists(file)).IsTrue();
    }

    /// <summary>
    /// A snapshot swept alongside was not written, so the file a delete would remove may be the
    /// only copy of it left. The delete stays pending, and the file stays where it is; a move is
    /// the snapshot arriving rather than the last copy leaving, so it goes ahead.
    /// </summary>
    [Test]
    public async Task AcceptAllHoldingDeletesLeavesThemPending()
    {
        await using var tracker = new RecordingTracker();
        ITrackedFiles tracked = tracker;
        tracker.AddDelete(file);
        await File.WriteAllTextAsync(temp, "content");
        tracker.AddMove(temp, target, null, null, false, null);

        var (accepted, kept) = tracked.AcceptAllTracked(holdDeletes: true);

        await Assert.That(accepted).IsEqualTo(1);
        await Assert.That(kept).IsEqualTo(1);
        await Assert.That(File.Exists(file)).IsTrue();
        await Assert.That(tracker.Deletes).HasSingleItem();
        await Assert.That(await File.ReadAllTextAsync(target)).IsEqualTo("content");
    }

    [Test]
    public async Task DiscardAllUntracksDeletesAndDropsMoveTemps()
    {
        await using var tracker = new RecordingTracker();
        ITrackedFiles tracked = tracker;
        tracker.AddDelete(file);
        await File.WriteAllTextAsync(temp, "content");
        tracker.AddMove(temp, target, null, null, false, null);

        var count = tracked.DiscardAll();

        await Assert.That(count).IsEqualTo(2);
        await Assert.That(File.Exists(file)).IsTrue();
        await Assert.That(File.Exists(temp)).IsFalse();
    }

    /// <summary>
    /// A pair the tray already tracks, arriving again over the viewer port. That message carries
    /// the two paths and nothing about the tool, and the tray filled the gap with its own choice
    /// for the extension - over a move that already said which tool was showing the pair. "Open
    /// diff tool" on a viewer pair does exactly this: it starts DiffEngineViewer --diff, which
    /// cannot bind the port and forwards the pair to the tray. The pair then read as some other
    /// tool's with no window open, and "Accept open" skipped it while it sat on screen.
    /// </summary>
    [Test]
    public async Task AMoveArrivingAgainOverTheViewerPortKeepsItsTool()
    {
        await using var tracker = new RecordingTracker();
        await File.WriteAllTextAsync(temp, "content");
        var arguments = $"--diff \"{temp}\" \"{target}\"";
        tracker.AddMove(temp, target, viewerExe, arguments, false, null);

        ((ITrackedFiles) tracker).AddMove(temp, target);

        var move = tracker.Moves.Single();
        await Assert.That(move.Exe).IsEqualTo(viewerExe);
        await Assert.That(move.Arguments).IsEqualTo(arguments);
        await Assert.That(move.CanKill).IsFalse();
        await Assert.That(move.IsViewer).IsTrue();
        await Assert.That(move.IsOpen).IsTrue();
    }

    /// <summary>
    /// The same for any other tool: what the sender said about it, and the process it started,
    /// are still the tool showing the pair.
    /// </summary>
    [Test]
    public async Task AMoveArrivingAgainOverTheViewerPortKeepsAnotherToolAndItsProcess()
    {
        await using var tracker = new RecordingTracker();
        await File.WriteAllTextAsync(temp, "content");
        // Stands in for the diff tool the sender started for the pair. A process of its own,
        // since the tray kills the tool of a move it may kill
        var tool = FileLockUtils.StartFileLockProcess(file);
        try
        {
            tracker.AddMove(temp, target, "theExe", "theArguments", true, tool.Id);

            ((ITrackedFiles) tracker).AddMove(temp, target);

            var move = tracker.Moves.Single();
            await Assert.That(move.Exe).IsEqualTo("theExe");
            await Assert.That(move.Arguments).IsEqualTo("theArguments");
            await Assert.That(move.CanKill).IsTrue();
            await Assert.That(move.IsViewer).IsFalse();
            await Assert.That(move.Process!.Id).IsEqualTo(tool.Id);
            await Assert.That(move.IsOpen).IsTrue();
        }
        finally
        {
            FileLockUtils.Cleanup(tool);
        }
    }

    /// <summary>
    /// What that message does say is taken: the target, and what is derived from it.
    /// </summary>
    [Test]
    public async Task AMoveArrivingAgainOverTheViewerPortTakesItsTarget()
    {
        await using var tracker = new RecordingTracker();
        await File.WriteAllTextAsync(temp, "content");
        tracker.AddMove(temp, target, viewerExe, "--diff", false, null);
        var moved = Path.Combine(Path.GetTempPath(), $"TrackedFilesTest_{Guid.NewGuid():N}.Other.verified.bin");

        ((ITrackedFiles) tracker).AddMove(temp, moved);

        var move = tracker.Moves.Single();
        await Assert.That(move.Target).IsEqualTo(moved);
        await Assert.That(move.Extension).IsEqualTo("bin");
        await Assert.That(move.Exe).IsEqualTo(viewerExe);
        await Assert.That(move.IsViewer).IsTrue();
    }

    // The copy bundled in some other project's DiffEngine package, which is where a sender's
    // viewer is and a path this process has never resolved
    static readonly string viewerExe = Path.Combine(
        Path.GetTempPath(),
        "some-other-package",
        "viewer",
        "DiffEngineViewer.exe");

    public void Dispose()
    {
        File.Delete(file);
        File.Delete(lateFile);
        if (File.Exists(temp))
        {
            File.Delete(temp);
        }

        if (File.Exists(target))
        {
            File.Delete(target);
        }

        FileEx.SafeDeleteDirectory(tempDirectory);
    }

    string file = Path.GetTempFileName();
    string lateFile = Path.GetTempFileName();
    string tempDirectory;
    string temp;
    string target;

    public TrackerTrackedFilesTest()
    {
        // The move's temp sits in its own directory, the way DiffEngine stages received files,
        // because accepting a move deletes that directory.
        tempDirectory = Path.Combine(Path.GetTempPath(), $"TrackedFilesTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        temp = Path.Combine(tempDirectory, "Sample.Test.received.txt");
        target = Path.Combine(Path.GetTempPath(), $"TrackedFilesTest_{Guid.NewGuid():N}.verified.txt");
    }

    /// <summary>
    /// With "Always kill locking processes" on, the menu accepts a locked move by killing
    /// the locker without asking. The same accept arriving from the viewer is refused as locked,
    /// because the preference lives inside the resolver and a wire accept never consults it.
    /// </summary>
    [Test]
    public async Task AlwaysKillAppliesToAnAcceptArrivingOverTheSocket()
    {
        var previous = LockedFilesHandler.AlwaysKill;
        // The stored preference, which Program loads into this at startup
        LockedFilesHandler.AlwaysKill = true;
        try
        {
            await File.WriteAllTextAsync(temp, "new");
            await File.WriteAllTextAsync(target, "old");
            // The resolver Program passes
            await using var tracker = new RecordingTracker(LockedFilesHandler.Resolve);
            var locker = FileLockUtils.StartFileLockProcess(target);
            try
            {
                tracker.AddMove(temp, target, "theExe", "theArguments", false, null);

                // What OwnedInlineHost does with an accept sent by a viewer displaying this queue
                var (ok, message) = ((ITrackedFiles) tracker).Accept(TrackedKeys.ForMove(temp));

                await Assert.That(message).IsNotEqualTo($"Files for '{Path.GetFileNameWithoutExtension(target)}' are locked. Accept from the tray menu to resolve.");
                await Assert.That(ok).IsTrue();
            }
            finally
            {
                FileLockUtils.Cleanup(locker);
            }

            await Assert.That(await File.ReadAllTextAsync(target)).IsEqualTo("new");
        }
        finally
        {
            LockedFilesHandler.AlwaysKill = previous;
        }
    }
}
