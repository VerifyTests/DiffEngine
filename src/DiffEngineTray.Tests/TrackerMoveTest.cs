using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

public class TrackerMoveTest :
    XunitContextBase
{
    [Fact]
    public async Task AddSingle()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddMove(file1, file1, "theExe", "theArguments", true, null);
        Assert.Equal(1, tracker.Moves.Count);
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
        var tracked = tracker.AddMove(file1, file1, "theExe", "theArguments", true, processId);
        Assert.Equal(1, tracker.Moves.Count);
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

    [Fact]
    public async Task AddSingle_BackgroundDelete()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddMove(file1, file1, "theExe", "theArguments", true, null);
        File.Delete(file1);
        Thread.Sleep(3000);
        tracker.AssertEmpty();
    }

    [Fact]
    public void AcceptSingle_NotEmpty()
    {
        var tracker = new RecordingTracker();
        var tracked = tracker.AddMove(file1, file1, "theExe", "theArguments", true, null);
        tracker.AddMove(file2, file2, "theExe", "theArguments", true, null);
        tracker.Accept(tracked);
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