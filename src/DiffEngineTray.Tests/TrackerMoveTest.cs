using System.IO;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

[Collection("Sequential")]
public class TrackerMoveTest :
    XunitContextBase
{
    [Fact]
    public void AddSingle()
    {
        var tracker = new RecordingTracker();
        tracker.AddMove(file1, file1, true, null, null);
        Assert.Equal(1, tracker.ActiveReceivedCount);
        Assert.Equal(0, tracker.InactiveReceivedCount);
        Assert.Equal(1, tracker.Moves.Count);
        Assert.True(tracker.TrackingAny);
    }

    [Fact]
    public void AddMultiple()
    {
        var tracker = new RecordingTracker();
        tracker.AddMove(file1, file1, true, null, null);
        tracker.AddMove(file2, file2, true, null, null);
        Assert.Equal(1, tracker.ActiveReceivedCount);
        Assert.Equal(0, tracker.InactiveReceivedCount);
        Assert.Equal(2, tracker.Moves.Count);
        Assert.True(tracker.TrackingAny);
    }

    [Fact]
    public void AddSame()
    {
        var tracker = new RecordingTracker();
        tracker.AddMove(file1, file1, true, null);
        var tracked = tracker.AddMove(file1, file1, true, 1, null);
        Assert.Equal(1, tracker.ActiveReceivedCount);
        Assert.Equal(0, tracker.InactiveReceivedCount);
        Assert.Equal(1, tracker.Moves.Count);
        Assert.Equal(1, tracked.ProcessId);
        Assert.True(tracker.TrackingAny);
    }

    [Fact]
    public void AcceptAllSingle()
    {
        var tracker = new RecordingTracker();
        tracker.AddMove(file1, file1, true, null, null);
        tracker.AcceptAll();
        Assert.Equal(1, tracker.ActiveReceivedCount);
        Assert.Equal(1, tracker.InactiveReceivedCount);
        tracker.AssertEmpty();
    }

    [Fact]
    public void AcceptAllMultiple()
    {
        var tracker = new RecordingTracker();
        tracker.AddMove(file1, file1, true, null, null);
        tracker.AddMove(file2, file2, true, null, null);
        tracker.AcceptAll();
        Assert.Equal(1, tracker.ActiveReceivedCount);
        Assert.Equal(1, tracker.InactiveReceivedCount);
        tracker.AssertEmpty();
    }

    [Fact]
    public void AcceptSingle()
    {
        var tracker = new RecordingTracker();
        var tracked = tracker.AddMove(file1, file1, true, null, null);
        tracker.Accept(tracked);
        Assert.Equal(1, tracker.ActiveReceivedCount);
        Assert.Equal(1, tracker.InactiveReceivedCount);
        tracker.AssertEmpty();
    }

    [Fact]
    public void AddSingle_BackgroundDelete()
    {
        var tracker = new RecordingTracker();
        tracker.AddMove(file1, file1, true, null, null);
        File.Delete(file1);
        Thread.Sleep(2100);
        Assert.Equal(1, tracker.ActiveReceivedCount);
        Assert.Equal(1, tracker.InactiveReceivedCount);
        tracker.AssertEmpty();
    }

    [Fact]
    public void AcceptSingle_NotEmpty()
    {
        var tracker = new RecordingTracker();
        var tracked = tracker.AddMove(file1, file1, true, null, null);
        tracker.AddMove(file2, file2, true, null, null);
        tracker.Accept(tracked);
        Assert.Equal(1, tracker.ActiveReceivedCount);
        Assert.Equal(0, tracker.InactiveReceivedCount);
        Assert.Equal(1, tracker.Moves.Count);
        Assert.True(tracker.TrackingAny);
    }

    public TrackerMoveTest(ITestOutputHelper output) :
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