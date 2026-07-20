// Covers the accept flow when the move fails without Restart Manager seeing a
// locker: a lock held by the current process is invisible to FindLockedFiles,
// which mimics a diff tool child process that is mid-death (handles releasing)
// or mid-startup (locks not taken yet).
public class TrackerAcceptRetryTest :
    IDisposable
{
    [Test]
    public async Task TransientLock_RetriesUntilAccepted()
    {
        await using var tracker = new RecordingTracker();
        var tracked = tracker.AddMove(temp, target, "theExe", "theArguments", false, null);

        // Hold the target exclusively (invisible to Restart Manager since the
        // lock lives in the current process), then release it while Accept is
        // still retrying
        var stream = File.Open(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var release = Task.Run(async () =>
        {
            await Task.Delay(1000);
            stream.Dispose();
        });

        tracker.Accept(tracked);
        await release;

        await tracker.AssertEmpty();
        await Assert.That(await File.ReadAllTextAsync(target)).IsEqualTo("new");
    }

    [Test]
    public async Task UnresolvedLock_KeepsMovePendingAndNotifies()
    {
        TrackedMove? failed = null;
        await using var tracker = new RecordingTracker(acceptFailed: move => failed = move);
        var tracked = tracker.AddMove(temp, target, "theExe", "theArguments", false, null);

        using (File.Open(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            tracker.Accept(tracked);
        }

        await Assert.That(tracker.Moves).HasSingleItem();
        await Assert.That(failed).IsNotNull();
        await Assert.That(failed!.Target).IsEqualTo(target);
        await Assert.That(await File.ReadAllTextAsync(target)).IsEqualTo("old");
    }

    static string CreateFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"TrackerAcceptRetryTest_{Guid.NewGuid()}.txt");
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        File.Delete(temp);
        File.Delete(target);
    }

    string temp = CreateFile("new");
    string target = CreateFile("old");
}
