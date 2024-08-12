[TestFixture]
public class TrackerDeleteTest :
    IDisposable
{
    [Test]
    public async Task AddSingle()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        AreEqual(tracker.Deletes,1);
        True(tracker.TrackingAny);
    }

    [Test]
    public async Task AddSingle_BackgroundDelete()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        File.Delete(file1);
        Thread.Sleep(5000);
        tracker.AssertEmpty();
    }

    [Test]
    public async Task AddMultiple()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        tracker.AddDelete(file2);
        AreEqual(2, tracker.Deletes.Count);
        True(tracker.TrackingAny);
    }

    [Test]
    public async Task AddSame()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        tracker.AddDelete(file1);
        AreEqual(1, tracker.Deletes);
        True(tracker.TrackingAny);
    }

    [Test]
    public async Task AcceptAllSingle()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        tracker.AcceptAll();
        tracker.AssertEmpty();
    }

    [Test]
    public async Task AcceptAllMultiple()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        tracker.AddDelete(file2);
        tracker.AcceptAll();
        tracker.AssertEmpty();
    }

    [Test]
    public async Task AcceptSingle()
    {
        await using var tracker = new RecordingTracker();
        var tracked = tracker.AddDelete(file1);
        tracker.Accept(tracked);
        tracker.AssertEmpty();
    }

    [Test]
    public async Task AcceptSingle_NotEmpty()
    {
        await using var tracker = new RecordingTracker();
        var tracked = tracker.AddDelete(file1);
        tracker.AddDelete(file2);
        tracker.Accept(tracked);
        AreEqual(1, tracker.Deletes);
        True(tracker.TrackingAny);
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