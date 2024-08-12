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
        IsEmpty(Deletes);
        IsEmpty(Moves);
        False(TrackingAny);
    }
}