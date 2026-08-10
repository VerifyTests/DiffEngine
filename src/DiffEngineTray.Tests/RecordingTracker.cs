class RecordingTracker(LockedFilesResolver? lockedFilesResolver = null, Action<TrackedMove>? acceptFailed = null, Action<string>? inlineFailed = null, IInlineHost? inline = null) :
    Tracker(
        () =>
        {
        },
        () =>
        {
        },
        lockedFilesResolver,
        acceptFailed,
        inlineFailed,
        inline)
{
    public async Task AssertEmpty()
    {
        await Assert.That(Deletes).IsEmpty();
        await Assert.That(Moves).IsEmpty();
        await Assert.That(Snapshots).IsEmpty();
        await Assert.That(TrackingAny).IsFalse();
    }
}
