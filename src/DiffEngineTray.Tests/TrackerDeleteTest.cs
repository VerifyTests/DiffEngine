
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
        tracker.AcceptAll();
        await tracker.AssertEmpty();
    }

    [Test]
    public async Task AcceptAllMultiple()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        tracker.AddDelete(file2);
        tracker.AcceptAll();
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
