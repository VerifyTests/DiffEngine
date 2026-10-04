/// <summary>
/// What a pending file was derived from, through the tracker: a page of a document whose document
/// is pending too. The tray only carries it. It lists the two as the two pending files they are,
/// and says which was derived from which to a viewer showing its queue, which is the one drawing
/// the document and so the one that puts a page beneath it.
/// </summary>
public class TrackerSourceTest :
    IDisposable
{
    [Test]
    public async Task AListingSaysWhatEachFileWasDerivedFrom()
    {
        await using var tracker = new RecordingTracker();
        ITrackedFiles tracked = tracker;
        tracker.AddMove(document, documentTarget, viewerExe, "--diff", false, null);
        tracker.AddMove(page, pageTarget, viewerExe, "--diff", false, null, document);
        tracker.AddDelete(stale, document);

        var moves = tracked.Moves().ToDictionary(_ => _.Temp);
        await Assert.That(moves[document].SourceKey).IsNull();
        // The key the document is listed under, so a reader matches one key against another
        await Assert.That(moves[page].SourceKey).IsEqualTo(moves[document].Key);
        await Assert.That(tracked.Deletes().Single().SourceKey).IsEqualTo(moves[document].Key);
    }

    [Test]
    public async Task AFileThatStandsAloneSaysNothing()
    {
        await using var tracker = new RecordingTracker();
        ITrackedFiles tracked = tracker;
        tracker.AddMove(page, pageTarget, viewerExe, "--diff", false, null);
        tracker.AddDelete(stale);

        await Assert.That(tracked.Moves().Single().SourceKey).IsNull();
        await Assert.That(tracked.Deletes().Single().SourceKey).IsNull();
    }

    /// <summary>
    /// A move that names its tool is a run saying everything it knows about the pair, so what it
    /// says of the source is taken, none included. A run whose document has stopped differing
    /// sends its pages with no source, and they stop being that document's.
    /// </summary>
    [Test]
    public async Task AMoveNamingItsToolSaysWhatItWasDerivedFromEachTime()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddMove(page, pageTarget, viewerExe, "--diff", false, null, document);
        await Assert.That(tracker.Moves.Single().Source).IsEqualTo(document);

        tracker.AddMove(page, pageTarget, viewerExe, "--diff", false, null);
        await Assert.That(tracker.Moves.Single().Source).IsNull();

        tracker.AddMove(page, pageTarget, viewerExe, "--diff", false, null, document);
        await Assert.That(tracker.Moves.Single().Source).IsEqualTo(document);
    }

    /// <summary>
    /// A move over the viewer port that names no source is the pair forwarded by something that
    /// was never told of one, "Open diff tool" among them, and says nothing about it either way.
    /// One that names a source says so.
    /// </summary>
    [Test]
    public async Task AMoveArrivingOverTheViewerPortKeepsItsSourceUnlessItNamesOne()
    {
        await using var tracker = new RecordingTracker();
        ITrackedFiles tracked = tracker;
        tracker.AddMove(page, pageTarget, viewerExe, "--diff", false, null, document);

        tracked.AddMove(page, pageTarget, null);
        await Assert.That(tracker.Moves.Single().Source).IsEqualTo(document);
        await Assert.That(tracker.Moves.Single().IsViewer).IsTrue();

        tracked.AddMove(page, pageTarget, other);
        await Assert.That(tracker.Moves.Single().Source).IsEqualTo(other);
    }

    [Test]
    public async Task AMoveSeenFirstOverTheViewerPortTakesItsSource()
    {
        await using var tracker = new RecordingTracker();
        ITrackedFiles tracked = tracker;

        tracked.AddMove(page, pageTarget, document);
        tracked.AddDelete(stale, document);

        await Assert.That(tracker.Moves.Single().Source).IsEqualTo(document);
        await Assert.That(tracker.Deletes.Single().Source).IsEqualTo(document);
    }

    /// <summary>
    /// Everything a listing carries of a delete is fixed on the object, which is what lets the
    /// objects tracked say whether a listing has changed. So a delete raised again under another
    /// source is another delete, and one raised again under the same source is the one it was.
    /// </summary>
    [Test]
    public async Task ADeleteRaisedAgainUnderAnotherSourceIsAnotherDelete()
    {
        await using var tracker = new RecordingTracker();
        ITrackedFiles tracked = tracker;
        var first = tracker.AddDelete(stale, document);
        var version = tracked.Version();

        var again = tracker.AddDelete(stale, document);
        await Assert.That(ReferenceEquals(again, first)).IsTrue();
        await Assert.That(tracked.Version()).IsEqualTo(version);

        var alone = tracker.AddDelete(stale);
        await Assert.That(ReferenceEquals(alone, first)).IsFalse();
        await Assert.That(alone.Source).IsNull();
        await Assert.That(tracked.Version()).IsNotEqualTo(version);
        await Assert.That(ReferenceEquals(tracker.Deletes.Single(), alone)).IsTrue();
    }

    /// <summary>
    /// A run that passes deletes the received files it had left and settles what a viewer was
    /// showing, which is the document. Its pages were tracked with no window to settle, so they
    /// go with it here, rather than standing in a viewer as a row each until the scan finds them
    /// gone.
    /// </summary>
    [Test]
    public async Task SettlingADocumentDropsWhatWasDerivedFromItAndHasGone()
    {
        await using var tracker = new RecordingTracker();
        ITrackedFiles tracked = tracker;
        tracker.AddMove(document, documentTarget, viewerExe, "--diff", false, null);
        tracker.AddMove(page, pageTarget, viewerExe, "--diff", false, null, document);
        // What the run did before it settled the document
        File.Delete(page);

        await Assert.That(tracked.Untrack(TrackedKeys.ForMove(document))).IsTrue();

        await Assert.That(tracker.Moves).IsEmpty();
    }

    /// <summary>
    /// Only what has gone. A page that still differs was written again by the same run before it
    /// settled the document, and is still a pending file.
    /// </summary>
    [Test]
    public async Task SettlingADocumentLeavesWhatWasDerivedFromItAndIsStillThere()
    {
        await using var tracker = new RecordingTracker();
        ITrackedFiles tracked = tracker;
        tracker.AddMove(document, documentTarget, viewerExe, "--diff", false, null);
        tracker.AddMove(page, pageTarget, viewerExe, "--diff", false, null, document);

        await Assert.That(tracked.Untrack(TrackedKeys.ForMove(document))).IsTrue();

        await Assert.That(tracker.Moves.Single().Temp).IsEqualTo(page);
    }

    /// <summary>
    /// And only what was derived from the move that went.
    /// </summary>
    [Test]
    public async Task SettlingAMoveLeavesWhatWasDerivedFromAnother()
    {
        await using var tracker = new RecordingTracker();
        ITrackedFiles tracked = tracker;
        tracker.AddMove(other, documentTarget, viewerExe, "--diff", false, null);
        tracker.AddMove(page, pageTarget, viewerExe, "--diff", false, null, document);
        File.Delete(page);

        await Assert.That(tracked.Untrack(TrackedKeys.ForMove(other))).IsTrue();

        await Assert.That(tracker.Moves.Single().Temp).IsEqualTo(page);
    }

    // The copy bundled in some other project's DiffEngine package, which is where a sender's
    // viewer is and a path this process has never resolved
    static readonly string viewerExe = Path.Combine(
        Path.GetTempPath(),
        "some-other-package",
        "viewer",
        "DiffEngineViewer.exe");

    readonly string directory;
    readonly string document;
    readonly string documentTarget;
    readonly string page;
    readonly string pageTarget;
    readonly string stale;
    readonly string other;

    public TrackerSourceTest()
    {
        directory = Path.Combine(Path.GetTempPath(), $"TrackerSourceTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        document = Path.Combine(directory, "Sample.Test.received.pdf");
        documentTarget = Path.Combine(directory, "Sample.Test.verified.pdf");
        page = Path.Combine(directory, "Sample.Test#page_0001.received.png");
        pageTarget = Path.Combine(directory, "Sample.Test#page_0001.verified.png");
        stale = Path.Combine(directory, "Sample.Test#page_0002.verified.png");
        other = Path.Combine(directory, "Other.Test.received.pdf");
        // Every file a test tracks is there, as a pending file's is. The tracker's scan drops a
        // move whose received file has gone and a delete whose file has, every two seconds, so a
        // test that wants one gone takes it away itself
        File.WriteAllText(document, "document");
        File.WriteAllText(page, "page");
        File.WriteAllText(other, "another document");
        File.WriteAllText(stale, "");
    }

    public void Dispose() =>
        FileEx.SafeDeleteDirectory(directory);
}
