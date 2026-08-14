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
        using (new FileStream(target, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            var (ok, message) = tracked.Accept(TrackedKeys.ForMove(temp));

            await Assert.That(ok).IsFalse();
            await Assert.That(message).Contains("locked");
            await Assert.That(message).Contains("tray menu");
            await Assert.That(tracker.Moves).HasSingleItem();
        }
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

        var (accepted, kept) = tracked.AcceptAll();

        await Assert.That(accepted).IsEqualTo(2);
        await Assert.That(kept).IsEqualTo(0);
        await Assert.That(File.Exists(file)).IsFalse();
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

    public void Dispose()
    {
        File.Delete(file);
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
}
