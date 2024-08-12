public class TrackerMoveTest :
    IDisposable
{
    [Fact]
    public async Task AddSingle()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddMove(file1, file1, "theExe", "theArguments", true, null);
        Assert.Single(tracker.Moves);
        Assert.True(tracker.TrackingAny);
    }

    [Fact]
    public async Task AddMultiple()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddMove(file1, file1, "theExe", "theArguments", true, null);
        tracker.AddMove(file2, file2, "theExe", "theArguments", true, null);
        Assert.Equal(2, tracker.Moves.Count);
        Assert.True(tracker.TrackingAny);
    }

    [Fact]
    public async Task AddSame()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddMove(file1, file1, "theExe", "theArguments", true, null);
        using var process = Process.GetCurrentProcess();
        var processId = process.Id;
        var tracked = tracker.AddMove(file1, file1, "theExe", "theArguments", false, processId);
        Assert.Single(tracker.Moves);
        Assert.Equal(process.Id, tracked.Process!.Id);
        Assert.True(tracker.TrackingAny);
    }

    [Fact]
    public async Task AcceptAllSingle()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddMove(file1, file1, "theExe", "theArguments", true, null);
        tracker.AcceptAll();
        tracker.AssertEmpty();
    }

    [Fact]
    public async Task AcceptAllMultiple()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddMove(file1, file1, "theExe", "theArguments", true, null);
        tracker.AddMove(file2, file2, "theExe", "theArguments", true, null);
        tracker.AcceptAll();
        tracker.AssertEmpty();
    }

    [Fact]
    public async Task AcceptSingle()
    {
        await using var tracker = new RecordingTracker();
        var tracked = tracker.AddMove(file1, file1, "theExe", "theArguments", true, null);
        tracker.Accept(tracked);
        tracker.AssertEmpty();
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

    [Fact]
    public async Task AddSingle_BackgroundDeleteTarget()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddMove(file1, file2, "theExe", "theArguments", true, null);
        File.Delete(file2);
        Thread.Sleep(3000);
        // many diff tools do not require a target.
        // so the non exist of a target file should not flush that item
        Assert.Single(tracker.Moves);
        Assert.True(tracker.TrackingAny);
    }

    [Fact]
    public async Task AcceptSingle_NotEmpty()
    {
        await using var tracker = new RecordingTracker();
        var tracked = tracker.AddMove(file1, file1, "theExe", "theArguments", true, null);
        tracker.AddMove(file2, file2, "theExe", "theArguments", true, null);
        tracker.Accept(tracked);
        Assert.Single(tracker.Moves);
        Assert.True(tracker.TrackingAny);
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