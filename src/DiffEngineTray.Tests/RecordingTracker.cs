using System.Threading;
using Xunit;

class RecordingTracker :
    Tracker
{
    public int ActiveReceivedCount;
    public int InactiveReceivedCount;

    public RecordingTracker() :
        base(null!, null!)
    {
        inactive = () => Interlocked.Increment(ref InactiveReceivedCount);
        active = () => Interlocked.Increment(ref ActiveReceivedCount);
    }

    public void AssertEmpty()
    {
        Assert.Empty(Deletes);
        Assert.Empty(Moves);
        Assert.False(TrackingAny);
    }
}