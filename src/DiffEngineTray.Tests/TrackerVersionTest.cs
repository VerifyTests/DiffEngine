/// <summary>
/// <see cref="ITrackedFiles.Version"/> is how a queue owner tells a displaying viewer that nothing
/// it listed has changed, so it has to move whenever a listing could have been built from
/// something other than what is tracked now.
/// <para>
/// An accept takes its move out of the tracker for as long as the move takes, which is seconds
/// when a file is locked, and puts the same object back when it could not be carried out. The
/// owner takes its tag and then builds its listing, so a listing built in that gap goes out
/// without the move under a tag taken with it. Compared by which objects are tracked, the tracker
/// then looked exactly as it had at the tag, and the viewer was told "unchanged" about a listing
/// that was missing a pending file, for as long as nothing else changed.
/// </para>
/// </summary>
public class TrackerVersionTest :
    IDisposable
{
    [Test]
    public async Task AMoveTakenOutAndPutBackIsAChange()
    {
        ITrackedFiles tracked = null!;
        int? listedWhileAccepting = null;
        // Told of the failure while the move is still out, which is where a listing built
        // between the two halves of an accept stands
        await using var tracker = new RecordingTracker(
            acceptFailed: _ => listedWhileAccepting = tracked.Moves().Count);
        tracked = tracker;
        var move = tracker.AddMove(temp, target, "theExe", "theArguments", false, null);
        // Read-only, so the accept gives up at once rather than after its retries
        File.SetAttributes(target, FileAttributes.ReadOnly);
        var before = tracked.Version();

        tracker.Accept(move);

        await Assert.That(listedWhileAccepting).IsEqualTo(0);
        await Assert.That(tracker.FindMove(temp)).IsSameReferenceAs(move);
        await Assert.That(tracked.Version()).IsNotEqualTo(before);
    }

    /// <summary>
    /// The same gap for a delete, which is untracked while its file is deleted and tracked again
    /// when that fails.
    /// </summary>
    [Test]
    public async Task ADeleteTakenOutAndPutBackIsAChange()
    {
        await using var tracker = new RecordingTracker();
        ITrackedFiles tracked = tracker;
        var delete = tracker.AddDelete(target);
        var before = tracked.Version();

        await using (File.Open(target, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            tracker.Accept(delete);
        }

        await Assert.That(tracker.Deletes).HasSingleItem();
        await Assert.That(tracked.Version()).IsNotEqualTo(before);
    }

    [Test]
    public async Task NothingHappeningIsNoChange()
    {
        await using var tracker = new RecordingTracker();
        ITrackedFiles tracked = tracker;
        tracker.AddMove(temp, target, "theExe", "theArguments", false, null);
        tracker.AddDelete(other);

        var before = tracked.Version();

        await Assert.That(tracked.Version()).IsEqualTo(before);
    }

    public TrackerVersionTest()
    {
        directory = Path.Combine(Path.GetTempPath(), "DiffEngineTray.Tests", Guid.NewGuid().ToString("N"));
        var received = Path.Combine(directory, "received");
        Directory.CreateDirectory(received);
        temp = Path.Combine(received, "file.received.txt");
        target = Path.Combine(directory, "file.verified.txt");
        other = Path.Combine(directory, "other.verified.txt");
        File.WriteAllText(temp, "received");
        File.WriteAllText(target, "verified");
        File.WriteAllText(other, "other");
    }

    public void Dispose()
    {
        File.SetAttributes(target, FileAttributes.Normal);
        Directory.Delete(directory, true);
    }

    readonly string directory;
    readonly string temp;
    readonly string target;
    readonly string other;
}
