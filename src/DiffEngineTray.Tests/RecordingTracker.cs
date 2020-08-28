using Xunit;

class RecordingTracker :
    Tracker
{
    public RecordingTracker() :
        base(() => {}, () => {})
    {
    }

    public void AssertEmpty()
    {
        Assert.Empty(Deletes);
        Assert.Empty(Moves);
        Assert.False(TrackingAny);
    }
}