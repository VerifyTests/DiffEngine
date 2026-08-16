/// <summary>
/// The tray driving a queue it does not own, which is what happens when a viewer bound the port
/// before this tray started. The other arrangement, where the tray holds the queue itself, is
/// covered by <see cref="OwnedInlineHostTest"/>.
/// <para>
/// These drive a real <see cref="FakeViewer"/> over a socket rather than staging files on disk.
/// </para>
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

        await tracker.Accept(snapshot);

        await Assert.That(viewer.Verbs).Contains($"accept:{snapshot.Key}");
        await Assert.That(tracker.Snapshots.Count).IsEqualTo(1);
        await Assert.That(tracker.Snapshots[0].Name).IsEqualTo("Other.cs:1");
    }

    /// <summary>
    /// Whether the entry survived is what tells an accept from a failure here, and that is a
    /// second round trip. An owner that took the accept and then could not answer used to come
    /// back as applied, because a listing that failed returned no items and no items read as an
    /// empty queue.
    /// </summary>
    [Test]
    public async Task AnAcceptThatCannotBeConfirmedIsNotCalledApplied()
    {
        using var viewer = new FakeViewer("Sample.cs:1");
        var host = new RemoteInlineHost();
        var snapshot = host.List().Single();
        viewer.ListingFails = true;

        var outcome = host.Accept(snapshot, out _);

        await Assert.That(outcome).IsEqualTo(AcceptOutcome.Unknown);
        await Assert.That(viewer.Verbs).Contains($"accept:{snapshot.Key}");
    }

    [Test]
    public async Task DiscardForwardsTheKey()
    {
        using var viewer = new FakeViewer("Sample.cs:1");
        await using var tracker = new RecordingTracker();
        var snapshot = tracker.Snapshots.Single();

        await tracker.Discard(snapshot);

        await Assert.That(viewer.Verbs).Contains($"discard:{snapshot.Key}");
        await Assert.That(tracker.Snapshots).IsEmpty();
    }

    /// <summary>
    /// The click returns before the queue has answered. Against a queue another process owns a
    /// discard is two socket round trips - the discard itself, then the listing the refresh reads -
    /// and both used to run on the thread that had just handled the menu click, which is the thread
    /// drawing everything else.
    /// </summary>
    [Test]
    public async Task DiscardDoesNotWaitOnTheQueue()
    {
        using var block = new ManualResetEventSlim();
        var host = new StubInlineHost(new PendingSnapshot("c:\\repo\\sample.cs|12", "Sample.cs:12", null))
        {
            DiscardBlock = block
        };
        await using var tracker = new RecordingTracker(inline: host);

        var discarding = tracker.Discard(tracker.Snapshots.Single());

        await Assert.That(host.DiscardStarted.Wait(TimeSpan.FromSeconds(5))).IsTrue();
        await Assert.That(discarding.IsCompleted).IsFalse();
        block.Set();
        await discarding;
    }

    [Test]
    public async Task AcceptAllForwardsOnce()
    {
        using var viewer = new FakeViewer("Sample.cs:1", "Other.cs:1", "Third.cs:1");
        await using var tracker = new RecordingTracker();

        await tracker.AcceptAllSnapshots();

        await Assert.That(viewer.Verbs).Contains("acceptall");
        await Assert.That(tracker.Snapshots).IsEmpty();
    }

    /// <summary>
    /// A sweep that wrote nothing has to reach the balloon. It used to return true and be silent:
    /// a refused patch counted as an accept and left the queue, so a click that wrote no snapshot
    /// anywhere emptied the menu and said nothing at all.
    /// </summary>
    [Test]
    public async Task ABulkAcceptThatWroteNothingIsReported()
    {
        var warnings = new List<string>();
        await using var tracker = new RecordingTracker(
            inlineFailed: warnings.Add,
            inline: new StubInlineHost(new PendingSnapshot("c:\\repo\\sample.cs|12", "Sample.cs:12", null))
            {
                AcceptAllSucceeds = false,
                AcceptAllMessage = "Accepted 0, 13 not written"
            });

        await tracker.AcceptAllSnapshots();

        await Assert.That(warnings).IsEquivalentTo(
            ["Could not accept the pending snapshots. Accepted 0, 13 not written"]);
    }

    [Test]
    public async Task AcceptAllWithNothingPendingDoesNotCallTheViewer()
    {
        using var viewer = new FakeViewer();
        await using var tracker = new RecordingTracker();

        await tracker.AcceptAllSnapshots();

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

        await tracker.Accept(snapshot);

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
        using (new FakeViewer("Sample.cs:1"))
        {
            await using var listing = new RecordingTracker();
            snapshot = listing.Snapshots.Single();
        }

        var failures = new List<string>();
        await using var tracker = new RecordingTracker(inlineFailed: failures.Add);
        await tracker.Accept(snapshot);

        await Assert.That(failures).HasSingleItem();
        await Assert.That(failures[0]).Contains("not running");
    }

    /// <summary>
    /// The menu counts snapshots in "Discard (n)", so discarding has to include them. Clearing
    /// only the local cache made the button lie twice over: fewer things went than it said, and
    /// the ones it skipped came back on the next scan.
    /// </summary>
    [Test]
    public async Task ClearDiscardsSnapshots()
    {
        using var viewer = new FakeViewer("Sample.cs:1");
        await using var tracker = new RecordingTracker();

        tracker.Clear();

        await Assert.That(viewer.Verbs).Contains("discardall");
        await Assert.That(viewer.Queue).IsEmpty();
        await Assert.That(tracker.Snapshots).IsEmpty();
    }

    [Test]
    public async Task GroupComesFromTheSourceFile()
    {
        var snapshot = new PendingSnapshot(@"c:\repo\solution\tests\sample.cs|42", "sample.cs:42", null);

        await Assert.That(snapshot.Source).IsEqualTo(@"c:\repo\solution\tests\sample.cs");
    }
}
