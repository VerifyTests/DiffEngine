using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

public class TrackerDeleteTest :
    XunitContextBase
{
    [Fact]
    public async Task AddSingle()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        Assert.Equal(1, tracker.Deletes.Count);
        Assert.True(tracker.TrackingAny);
    }

    [Fact]
    public async Task AddSingle_BackgroundDelete()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        File.Delete(file1);
        Thread.Sleep(3000);
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
    public void AddSame()
    {
        var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        tracker.AddDelete(file1);
        Assert.Equal(1, tracker.Deletes.Count);
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
        Assert.Equal(1, tracker.Deletes.Count);
        Assert.True(tracker.TrackingAny);
    }

    public TrackerDeleteTest(ITestOutputHelper output) :
        base(output)
    {
        file1 = Path.GetTempFileName();
        file2 = Path.GetTempFileName();
        file3 = Path.GetTempFileName();
    }

    public override void Dispose()
    {
        File.Delete(file1);
        File.Delete(file2);
        File.Delete(file3);
        base.Dispose();
    }

    string file1;
    string file2;
    string file3;
}