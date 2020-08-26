using System.IO;
using Xunit;
using Xunit.Abstractions;

public class TrackerMoveTest :
    XunitContextBase
{
    [Fact]
    public void AddSingle()
    {
        var tracker = new RecordingTracker();
        tracker.AddMove(file1, file1, true, null);
        Assert.Equal(1, RecordingTracker.ActiveReceivedCount);
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