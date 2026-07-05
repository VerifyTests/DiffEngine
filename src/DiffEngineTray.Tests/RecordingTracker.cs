class RecordingTracker(LockedFilesResolver? lockedFilesResolver = null) :
    Tracker(
        () =>
        {
        },
        () =>
        {
        },
        lockedFilesResolver)
{
    public async Task AssertEmpty()
    {
        await Assert.That(Deletes).IsEmpty();
        await Assert.That(Moves).IsEmpty();
        await Assert.That(TrackingAny).IsFalse();
    }
}