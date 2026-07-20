class RecordingTracker(LockedFilesResolver? lockedFilesResolver = null, Action<TrackedMove>? acceptFailed = null) :
    Tracker(
        () =>
        {
        },
        () =>
        {
        },
        lockedFilesResolver,
        acceptFailed)
{
    public async Task AssertEmpty()
    {
        await Assert.That(Deletes).IsEmpty();
        await Assert.That(Moves).IsEmpty();
        await Assert.That(TrackingAny).IsFalse();
    }
}