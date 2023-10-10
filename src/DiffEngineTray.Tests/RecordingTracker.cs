class RecordingTracker() :
    Tracker(
        () =>
        {
        },
        () =>
        {
        })
{
    public void AssertEmpty()
    {
        Assert.Empty(Deletes);
        Assert.Empty(Moves);
        Assert.False(TrackingAny);
    }
}