/// <summary>
/// A move that cannot be killed still has a process: DiffRunner sends the id for an MDI tool too,
/// and <see cref="ProcessEx.TryGet"/> holds a handle on it. Nothing let go of
/// that handle when the move left, since the only place a tracked process was disposed was the
/// kill these moves are passed over for. One handle a tracked move, until a finaliser ran.
/// <para>
/// Every process here is one the test started, and none of them may be ended by the tray: the
/// move says it cannot be killed.
/// </para>
/// </summary>
[NotInParallel]
public class TrackerProcessReleaseTest :
    IDisposable
{
    [Test]
    public async Task AnAcceptedMoveLetsGoOfItsProcess()
    {
        await using var tracker = new RecordingTracker();
        var tracked = Track(tracker);
        var held = tracked.Process!;

        tracker.Accept(tracked);

        await AssertReleased(tracked, held);
    }

    [Test]
    public async Task ADiscardedMoveLetsGoOfItsProcess()
    {
        await using var tracker = new RecordingTracker();
        var tracked = Track(tracker);
        var held = tracked.Process!;

        tracker.Discard(tracked);

        await AssertReleased(tracked, held);
    }

    [Test]
    public async Task ASettledMoveLetsGoOfItsProcess()
    {
        await using var tracker = new RecordingTracker();
        var tracked = Track(tracker);
        var held = tracked.Process!;

        await Assert.That(((ITrackedFiles) tracker).Untrack(TrackedKeys.ForMove(temp))).IsTrue();

        await AssertReleased(tracked, held);
    }

    [Test]
    public async Task AMoveStillTrackedWhenTheTrayExitsLetsGoOfItsProcess()
    {
        var tracker = new RecordingTracker();
        var tracked = Track(tracker);
        var held = tracked.Process!;

        await tracker.DisposeAsync();

        await AssertReleased(tracked, held);
    }

    /// <summary>
    /// A move kept pending keeps its process, which is what "Accept all open" and "Open diff tool"
    /// read to find the window.
    /// </summary>
    [Test]
    public async Task AMoveKeptPendingKeepsItsProcess()
    {
        await using var tracker = new RecordingTracker();
        var tracked = Track(tracker);
        // Nowhere to move the file to, so the accept is refused and the move stays
        Directory.Delete(Path.GetDirectoryName(target)!, true);

        tracker.Accept(tracked);

        await Assert.That(tracker.Moves).HasSingleItem();
        await Assert.That(tracked.Process).IsNotNull();
        await Assert.That(tracked.Process!.HasExited).IsFalse();
    }

    TrackedMove Track(Tracker tracker)
    {
        var tracked = tracker.AddMove(temp, target, tool.MainModule!.FileName, "theArguments", false, tool.Id);
        if (tracked.Process is null)
        {
            throw new("The process was not tracked.");
        }

        return tracked;
    }

    async Task AssertReleased(TrackedMove tracked, Process held)
    {
        await Assert.That(tracked.Process).IsNull();
        // What a disposed Process says of anything asked about the process it held
        await Assert.That(() => held.HasExited).Throws<InvalidOperationException>();
        // Let go of, and not ended
        await Assert.That(tool.HasExited).IsFalse();
    }

    public TrackerProcessReleaseTest()
    {
        directory = Path.Combine(Path.GetTempPath(), "DiffEngineTray.Tests", Guid.NewGuid().ToString("N"));
        var received = Path.Combine(directory, "received");
        var verified = Path.Combine(directory, "verified");
        Directory.CreateDirectory(received);
        Directory.CreateDirectory(verified);
        temp = Path.Combine(received, "file.txt");
        target = Path.Combine(verified, "file.txt");
        File.WriteAllText(temp, "received");
        File.WriteAllText(target, "verified");
        var locked = Path.Combine(directory, "locked.txt");
        File.WriteAllText(locked, "");
        tool = FileLockUtils.StartFileLockProcess(locked, shows: temp);
    }

    public void Dispose()
    {
        FileLockUtils.Cleanup(tool);
        try
        {
            Directory.Delete(directory, true);
        }
        catch (IOException)
        {
            // The process that held a file in it is still on its way out
        }
    }

    readonly string directory;
    readonly string temp;
    readonly string target;
    readonly Process tool;
}
