public class TrackerDeleteTest(ITestOutputHelper output) :
    XunitContextBase(output)
{
    [Fact]
    public async Task AddSingle()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        Assert.Single(tracker.Deletes);
        Assert.True(tracker.TrackingAny);
    }

    [Fact]
    public async Task AddSingle_BackgroundDelete()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        File.Delete(file1);
        Thread.Sleep(5000);
        tracker.AssertEmpty();
    }

    [Fact]
    public async Task AddMultiple()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        tracker.AddDelete(file2);
        Assert.Equal(2, tracker.Deletes.Count);
        Assert.True(tracker.TrackingAny);
    }

    [Fact]
    public async Task AddSame()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        tracker.AddDelete(file1);
        Assert.Single(tracker.Deletes);
        Assert.True(tracker.TrackingAny);
    }

    [Fact]
    public async Task AcceptAllSingle()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        tracker.AcceptAll();
        tracker.AssertEmpty();
    }

    [Fact]
    public async Task AcceptAllMultiple()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        tracker.AddDelete(file2);
        tracker.AcceptAll();
        tracker.AssertEmpty();
    }

    [Fact]
    public async Task AcceptSingle()
    {
        await using var tracker = new RecordingTracker();
        var tracked = tracker.AddDelete(file1);
        tracker.Accept(tracked);
        tracker.AssertEmpty();
    }

    [Fact]
    public async Task AcceptSingle_NotEmpty()
    {
        await using var tracker = new RecordingTracker();
        var tracked = tracker.AddDelete(file1);
        tracker.AddDelete(file2);
        tracker.Accept(tracked);
        Assert.Single(tracker.Deletes);
        Assert.True(tracker.TrackingAny);
    }

    public override void Dispose()
    {
        File.Delete(file1);
        File.Delete(file2);
        File.Delete(file3);
        base.Dispose();
    }

    string file1 = Path.GetTempFileName();
    string file2 = Path.GetTempFileName();
    string file3 = Path.GetTempFileName();
}