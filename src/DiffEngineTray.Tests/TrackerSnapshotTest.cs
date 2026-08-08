/// <summary>
/// The tray no longer stores pending inline snapshots; the viewer owns the queue and the tray
/// drives it over the socket. These drive a real <see cref="FakeViewer"/> rather than staging
/// files on disk.
/// </summary>
public class TrackerSnapshotTest
{
    [Test]
    public async Task ListsWhatTheViewerHasPending()
    {
        using var viewer = new FakeViewer("Sample.cs:1", "Other.cs:1");
        await using var tracker = new RecordingTracker();

        await Assert.That(tracker.Snapshots.Count).IsEqualTo(2);
        await Assert.That(tracker.TrackingAny).IsTrue();
    }

    [Test]
    public async Task NoViewerMeansNothingPending()
    {
        await using var tracker = new RecordingTracker();

        await tracker.AssertEmpty();
    }

    [Test]
    public async Task AcceptForwardsTheKeyAndRefreshes()
    {
        using var viewer = new FakeViewer("Sample.cs:1", "Other.cs:1");
        await using var tracker = new RecordingTracker();
        var snapshot = tracker.Snapshots.Single(_ => _.Name == "Sample.cs:1");

        tracker.Accept(snapshot);

        await Assert.That(viewer.Verbs).Contains($"accept:{snapshot.Key}");
        await Assert.That(tracker.Snapshots.Count).IsEqualTo(1);
        await Assert.That(tracker.Snapshots[0].Name).IsEqualTo("Other.cs:1");
    }

    [Test]
    public async Task DiscardForwardsTheKey()
    {
        using var viewer = new FakeViewer("Sample.cs:1");
        await using var tracker = new RecordingTracker();
        var snapshot = tracker.Snapshots.Single();

        tracker.Discard(snapshot);

        await Assert.That(viewer.Verbs).Contains($"discard:{snapshot.Key}");
        await Assert.That(tracker.Snapshots).IsEmpty();
    }

    [Test]
    public async Task AcceptAllForwardsOnce()
    {
        using var viewer = new FakeViewer("Sample.cs:1", "Other.cs:1", "Third.cs:1");
        await using var tracker = new RecordingTracker();

        tracker.AcceptAllSnapshots();

        await Assert.That(viewer.Verbs).Contains("acceptall");
        await Assert.That(tracker.Snapshots).IsEmpty();
    }

    [Test]
    public async Task AcceptAllWithNothingPendingDoesNotCallTheViewer()
    {
        using var viewer = new FakeViewer();
        await using var tracker = new RecordingTracker();

        tracker.AcceptAllSnapshots();

        await Assert.That(viewer.Verbs).DoesNotContain("acceptall");
    }

    [Test]
    public async Task AFailedAcceptNotifiesAndLeavesItPending()
    {
        using var viewer = new FakeViewer("Sample.cs:1")
        {
            Succeed = false
        };
        var failures = new List<string>();
        await using var tracker = new RecordingTracker(inlineFailed: failures.Add);
        var snapshot = tracker.Snapshots.Single();

        tracker.Accept(snapshot);

        await Assert.That(failures).HasSingleItem();
        await Assert.That(failures[0]).Contains("Sample.cs:1");
        await Assert.That(failures[0]).Contains("the file is locked");
        await Assert.That(tracker.Snapshots).HasSingleItem();
    }

    /// <summary>
    /// A viewer that went away between the menu opening and the click must report rather than
    /// throw.
    /// </summary>
    [Test]
    public async Task ActingAfterTheViewerExitedNotifies()
    {
        PendingSnapshot snapshot;
        using (var viewer = new FakeViewer("Sample.cs:1"))
        {
            await using var listing = new RecordingTracker();
            snapshot = listing.Snapshots.Single();
        }

        var failures = new List<string>();
        await using var tracker = new RecordingTracker(inlineFailed: failures.Add);
        tracker.Accept(snapshot);

        await Assert.That(failures).HasSingleItem();
        await Assert.That(failures[0]).Contains("not running");
    }

    /// <summary>
    /// Clear drops what the tray tracks. The viewer is a separate process the user can still act
    /// on, so its queue is deliberately left alone.
    /// </summary>
    [Test]
    public async Task ClearLeavesTheViewerQueueAlone()
    {
        using var viewer = new FakeViewer("Sample.cs:1");
        await using var tracker = new RecordingTracker();

        tracker.Clear();

        await Assert.That(viewer.Queue).HasSingleItem();
        await Assert.That(viewer.Verbs).DoesNotContain("discardall");
    }

    [Test]
    public async Task GroupComesFromTheSourceFile()
    {
        var snapshot = new PendingSnapshot(@"c:\repo\solution\tests\sample.cs|42", "sample.cs:42", null);

        await Assert.That(snapshot.Source).IsEqualTo(@"c:\repo\solution\tests\sample.cs");
    }
}
