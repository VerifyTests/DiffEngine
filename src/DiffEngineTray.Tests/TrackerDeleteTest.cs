
public class TrackerDeleteTest :
    IDisposable
{
    [Test]
    public async Task AddSingle()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        await Assert.That(tracker.Deletes).HasSingleItem();
        await Assert.That(tracker.TrackingAny).IsTrue();
    }

    [Test]
    public async Task AddSingle_BackgroundDelete()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        File.Delete(file1);
        Thread.Sleep(5000);
        await tracker.AssertEmpty();
    }

    [Test]
    public async Task AddMultiple()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        tracker.AddDelete(file2);
        await Assert.That(tracker.Deletes.Count).IsEqualTo(2);
        await Assert.That(tracker.TrackingAny).IsTrue();
    }

    [Test]
    public async Task AddSame()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        tracker.AddDelete(file1);
        await Assert.That(tracker.Deletes).HasSingleItem();
        await Assert.That(tracker.TrackingAny).IsTrue();
    }

    [Test]
    public async Task AcceptAllSingle()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        await tracker.AcceptAll();
        await tracker.AssertEmpty();
    }

    [Test]
    public async Task AcceptAllMultiple()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        tracker.AddDelete(file2);
        await tracker.AcceptAll();
        await tracker.AssertEmpty();
    }

    [Test]
    public async Task AcceptSingle()
    {
        await using var tracker = new RecordingTracker();
        var tracked = tracker.AddDelete(file1);
        tracker.Accept(tracked);
        await tracker.AssertEmpty();
    }

    [Test]
    public async Task AcceptSingle_NotEmpty()
    {
        await using var tracker = new RecordingTracker();
        var tracked = tracker.AddDelete(file1);
        tracker.AddDelete(file2);
        tracker.Accept(tracked);
        await Assert.That(tracker.Deletes).HasSingleItem();
        await Assert.That(tracker.TrackingAny).IsTrue();
    }

    /// <summary>
    /// A delete that cannot be done. The menu and hot key paths called File.Delete straight, so a
    /// read-only or open verified file threw out of the click handler - onto the UI thread, where
    /// nothing hooks Application.ThreadException - and the entry had already been untracked, so
    /// the pending delete went with it.
    /// </summary>
    [Test]
    public async Task AcceptLeavesAnUndeletableFileTracked()
    {
        await using var tracker = new RecordingTracker();
        var tracked = tracker.AddDelete(file1);

        await using (File.Open(file1, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            tracker.Accept(tracked);
        }

        await Assert.That(tracker.Deletes).HasSingleItem();
        await Assert.That(File.Exists(file1)).IsTrue();
    }

    /// <summary>
    /// And one bad delete does not stop the rest of Accept all. Unguarded, the throw skipped the
    /// remaining deletes, every pending move, and the snapshots.
    /// </summary>
    [Test]
    public async Task AcceptAllContinuesPastAnUndeletableFile()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        tracker.AddDelete(file2);

        await using (File.Open(file1, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await tracker.AcceptAll();
        }

        // The one that could go, went; the one that could not is still pending
        await Assert.That(File.Exists(file2)).IsFalse();
        await Assert.That(tracker.Deletes).HasSingleItem();
    }

    /// <summary>
    /// A snapshot moving inline arrives as a patch plus a delete of the verified file it replaces,
    /// and "Accept all" used to delete first. The snapshots go first now, while that file is still
    /// there to fall back on.
    /// </summary>
    [Test]
    public async Task AcceptAllAppliesTheSnapshotsBeforeTheDeletes()
    {
        bool? existedWhileAccepting = null;
        await using var tracker = new RecordingTracker(
            inline: new StubInlineHost(new PendingSnapshot(@"c:\repo\sample.cs|12", "Sample.cs:12", null))
            {
                AcceptingAll = () => existedWhileAccepting = File.Exists(file1)
            });
        tracker.AddDelete(file1);

        await tracker.AcceptAll();

        await Assert.That(existedWhileAccepting).IsTrue();
        await Assert.That(File.Exists(file1)).IsFalse();
    }

    /// <summary>
    /// A patch was refused, so the file a delete would remove may be the only copy of that snapshot
    /// left. The delete stays pending, the file stays where it is, and the balloon says why.
    /// </summary>
    [Test]
    public async Task AcceptAllHoldsTheDeletesWhenASnapshotWasNotWritten()
    {
        var warnings = new List<string>();
        await using var tracker = new RecordingTracker(
            inlineFailed: warnings.Add,
            inline: new StubInlineHost(new PendingSnapshot(@"c:\repo\sample.cs|12", "Sample.cs:12", null))
            {
                AcceptAllSucceeds = false,
                AcceptAllRefuses = true,
                AcceptAllMessage = "Accepted 0, 1 not written"
            });
        tracker.AddDelete(file1);

        await tracker.AcceptAll();

        await Assert.That(File.Exists(file1)).IsTrue();
        await Assert.That(tracker.Deletes).HasSingleItem();
        await Assert.That(warnings).IsEquivalentTo(
            [$"Could not accept the pending snapshots. Accepted 0, 1 not written {Tracker.DeletesHeld}"]);
    }

    public void Dispose()
    {
        File.Delete(file1);
        File.Delete(file2);
        File.Delete(file3);
    }

    string file1 = Path.GetTempFileName();
    string file2 = Path.GetTempFileName();
    string file3 = Path.GetTempFileName();
}
