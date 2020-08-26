using System.Threading;
using Xunit;

class RecordingTracker :
    Tracker
{
    public static int ActiveReceivedCount;
    public static int InactiveReceivedCount;

    public RecordingTracker() :
        base(
            () => Interlocked.Increment(ref ActiveReceivedCount),
            () => Interlocked.Increment(ref InactiveReceivedCount))
    {
        ActiveReceivedCount = 0;
        InactiveReceivedCount = 0;
    }

    public void AssertEmpty()
    {
        Assert.Empty(Deletes);
        Assert.Empty(Moves);
        Assert.False(TrackingAny);
    }
}