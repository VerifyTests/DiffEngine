class RecordingTracker(LockedFilesResolver? lockedFilesResolver = null, Action<TrackedMove>? acceptFailed = null, Action<TrackedInlineMove, string>? inlineFailed = null) :
    Tracker(
        () =>
        {
        },
        () =>
        {
        },
        lockedFilesResolver,
        acceptFailed,
        inlineFailed)
{
    public async Task AssertEmpty()
    {
        await Assert.That(Deletes).IsEmpty();
        await Assert.That(Moves).IsEmpty();
        await Assert.That(InlineMoves).IsEmpty();
        await Assert.That(TrackingAny).IsFalse();
    }
}