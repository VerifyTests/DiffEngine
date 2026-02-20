public class TrackerMoveTest :
    IDisposable
{
    [Test]
    public async Task AddSingle()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddMove(file1, file1, "theExe", "theArguments", true, null);
        await Assert.That(tracker.Moves).HasSingleItem();
        await Assert.That(tracker.TrackingAny).IsTrue();
    }

    [Test]
    public async Task AddMultiple()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddMove(file1, file1, "theExe", "theArguments", true, null);
        tracker.AddMove(file2, file2, "theExe", "theArguments", true, null);
        await Assert.That(tracker.Moves.Count).IsEqualTo(2);
        await Assert.That(tracker.TrackingAny).IsTrue();
    }

    [Test]
    public async Task AddSame()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddMove(file1, file1, "theExe", "theArguments", true, null);
        using var process = Process.GetCurrentProcess();
        var processId = process.Id;
        var tracked = tracker.AddMove(file1, file1, "theExe", "theArguments", false, processId);
        await Assert.That(tracker.Moves).HasSingleItem();
        await Assert.That(tracked.Process!.Id).IsEqualTo(process.Id);
        await Assert.That(tracker.TrackingAny).IsTrue();
    }

    [Test]
    public async Task AcceptAllSingle()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddMove(file1, file1, "theExe", "theArguments", true, null);
        tracker.AcceptAll();
        await tracker.AssertEmpty();
    }

    [Test]
    public async Task AcceptAllMultiple()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddMove(file1, file1, "theExe", "theArguments", true, null);
        tracker.AddMove(file2, file2, "theExe", "theArguments", true, null);
        tracker.AcceptAll();
        await tracker.AssertEmpty();
    }

    [Test]
    public async Task AcceptSingle()
    {
        await using var tracker = new RecordingTracker();
        var tracked = tracker.AddMove(file1, file1, "theExe", "theArguments", true, null);
        tracker.Accept(tracked);
        await tracker.AssertEmpty();
    }

    // [Fact]
    // public async Task AddSingle_BackgroundDeleteTemp()
    // {
    //     await using var tracker = new RecordingTracker();
    //     tracker.AddMove(file1, file2, "theExe", "theArguments", true, null);
    //     File.Delete(file1);
    //     Thread.Sleep(3000);
    //     tracker.AssertEmpty();
    // }

    [Test]
    public async Task AddSingle_BackgroundDeleteTarget()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddMove(file1, file2, "theExe", "theArguments", true, null);
        File.Delete(file2);
        Thread.Sleep(3000);
        // many diff tools do not require a target.
        // so the non exist of a target file should not flush that item
        await Assert.That(tracker.Moves).HasSingleItem();
        await Assert.That(tracker.TrackingAny).IsTrue();
    }

    [Test]
    public async Task AcceptSingle_NotEmpty()
    {
        await using var tracker = new RecordingTracker();
        var tracked = tracker.AddMove(file1, file1, "theExe", "theArguments", true, null);
        tracker.AddMove(file2, file2, "theExe", "theArguments", true, null);
        tracker.Accept(tracked);
        await Assert.That(tracker.Moves).HasSingleItem();
        await Assert.That(tracker.TrackingAny).IsTrue();
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
