public class TrackerClearTest :
    IDisposable
{
    [Test]
    public async Task Simple()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        tracker.AddMove(file2, file2, "theExe", "theArguments", true, null);
        await tracker.Clear();
        await tracker.AssertEmpty();
    }

    /// <summary>
    /// "Discard (n)" is a menu click or a hot key, so the thread that asks is the one drawing
    /// everything. Discarding a move ends its diff tool and waits up to half a second for it to
    /// go, and the moves were discarded before the call returned: only the queue's half had been
    /// moved to a worker.
    /// <para>
    /// Asked from a thread of its own, which is never the worker's. Asked from the pool, the
    /// thread that asked can be the one that picks the work up once the test awaits.
    /// </para>
    /// </summary>
    [Test]
    public async Task TheFilesAreNotDiscardedOnTheThreadThatAsked()
    {
        await using var tracker = new WatchedTracker();
        tracker.AddDelete(file1);
        tracker.AddMove(file2, file2, "theExe", "theArguments", true, null);

        var asked = new TaskCompletionSource<(int thread, Task clearing)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => asked.SetResult((Environment.CurrentManagedThreadId, tracker.Clear())))
        {
            IsBackground = true
        };
        thread.Start();
        var (caller, clearing) = await asked.Task;
        await clearing;

        await Assert.That(tracker.DiscardedOn).IsNotNull();
        await Assert.That(tracker.DiscardedOn).IsNotEqualTo(caller);
        await tracker.AssertEmpty();
    }

    /// <summary>
    /// A queue that would not discard, or could not be reached, keeps its snapshots, and the menu
    /// goes on listing them under a button that had just been pressed to be rid of them. The log
    /// was the only place that said why.
    /// </summary>
    [Test]
    public async Task ABulkDiscardTheQueueDidNotCarryOutIsSaid()
    {
        var warnings = new ConcurrentQueue<string>();
        await using var tracker = new RecordingTracker(
            inlineFailed: warnings.Enqueue,
            inline: new StubInlineHost(new PendingSnapshot(@"c:\repo\sample.cs|12", "Sample.cs:12", null))
            {
                DiscardAllSucceeds = false,
                DiscardAllMessage = "The snapshot viewer did not answer."
            });
        tracker.AddDelete(file1);

        await tracker.Clear();

        await Assert.That(warnings).IsEquivalentTo(
            ["Could not discard the pending snapshots. The snapshot viewer did not answer."]);
        // The files went all the same, and the snapshots are still what the menu shows
        await Assert.That(tracker.Deletes).IsEmpty();
        await Assert.That(tracker.Snapshots).HasSingleItem();
    }

    [Test]
    public async Task ABulkDiscardThatWorkedSaysNothing()
    {
        var warnings = new ConcurrentQueue<string>();
        await using var tracker = new RecordingTracker(
            inlineFailed: warnings.Enqueue,
            inline: new StubInlineHost(new PendingSnapshot(@"c:\repo\sample.cs|12", "Sample.cs:12", null)));

        await tracker.Clear();

        await Assert.That(warnings).IsEmpty();
        await Assert.That(tracker.Snapshots).IsEmpty();
    }

    /// <summary>
    /// A tracker that says which thread its files were discarded on.
    /// </summary>
    class WatchedTracker :
        RecordingTracker
    {
        public int? DiscardedOn { get; private set; }

        protected override int DiscardFiles()
        {
            DiscardedOn = Environment.CurrentManagedThreadId;
            return base.DiscardFiles();
        }
    }

    public void Dispose()
    {
        File.Delete(file1);
        File.Delete(file2);
    }

    string file1 = Path.GetTempFileName();
    string file2 = Path.GetTempFileName();
}
