using System.IO;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

public class TrackerDeleteTest :
    XunitContextBase
{
    [Fact]
    public void AddSingle()
    {
        var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        Assert.Equal(1, tracker.ActiveReceivedCount);
        Assert.Equal(0, tracker.InactiveReceivedCount);
        Assert.Equal(1, tracker.Deletes.Count);
        Assert.True(tracker.TrackingAny);
    }

    [Fact]
    public void AddSingle_BackgroundDelete()
    {
        var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        File.Delete(file1);
        Thread.Sleep(2100);
        Assert.Equal(1, tracker.ActiveReceivedCount);
        Assert.Equal(1, tracker.InactiveReceivedCount);
        tracker.AssertEmpty();
    }

    [Fact]
    public void AddMultiple()
    {
        var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        tracker.AddDelete(file2);
        Assert.Equal(1, tracker.ActiveReceivedCount);
        Assert.Equal(0, tracker.InactiveReceivedCount);
        Assert.Equal(2, tracker.Deletes.Count);
        Assert.True(tracker.TrackingAny);
    }

    [Fact]
    public void AddSame()
    {
        var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        tracker.AddDelete(file1);
        Assert.Equal(1, tracker.ActiveReceivedCount);
        Assert.Equal(0, tracker.InactiveReceivedCount);
        Assert.Equal(1, tracker.Deletes.Count);
        Assert.True(tracker.TrackingAny);
    }

    [Fact]
    public void AcceptAllSingle()
    {
        var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        tracker.AcceptAll();
        Assert.Equal(1, tracker.ActiveReceivedCount);
        Assert.Equal(1, tracker.InactiveReceivedCount);
        tracker.AssertEmpty();
    }

    [Fact]
    public void AcceptAllMultiple()
    {
        var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        tracker.AddDelete(file2);
        tracker.AcceptAll();
        Assert.Equal(1, tracker.ActiveReceivedCount);
        Assert.Equal(1, tracker.InactiveReceivedCount);
        tracker.AssertEmpty();
    }

    [Fact]
    public void AcceptSingle()
    {
        var tracker = new RecordingTracker();
        var tracked = tracker.AddDelete(file1);
        tracker.Accept(tracked);
        Assert.Equal(1, tracker.ActiveReceivedCount);
        Assert.Equal(1, tracker.InactiveReceivedCount);
        tracker.AssertEmpty();
    }

    [Fact]
    public void AcceptSingle_NotEmpty()
    {
        var tracker = new RecordingTracker();
        var tracked = tracker.AddDelete(file1);
        tracker.AddDelete(file2);
        tracker.Accept(tracked);
        Assert.Equal(1, tracker.ActiveReceivedCount);
        Assert.Equal(0, tracker.InactiveReceivedCount);
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