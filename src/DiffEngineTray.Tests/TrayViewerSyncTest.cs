extern alias viewer;

using System.Collections.Concurrent;

// The viewer's own copies of the protocol types. DiffEngineViewer links DiffEngine's Inline and
// Protocol sources rather than referencing them, so the same names exist in both assemblies and
// only the alias tells them apart. That duplication is the point of these tests: the two halves
// are compiled separately and only ever meet over a socket.
using ViewerSideApplyResult = viewer::DiffEngine.InlineApplyResult;
using ViewerSidePatch = viewer::DiffEngine.InlinePatch;
using ViewerSideServer = viewer::DiffEngine.ViewerServer;
using ViewerSideVerb = viewer::DiffEngine.ViewerVerb;
using ViewerSideWindowCommand = viewer::DiffEngine.WindowCommand;

// The viewer's own half of the app.
using CommandKind = viewer::CommandKind;
using OwnerLink = viewer::OwnerLink;
using SessionHost = viewer::SessionHost;
using SessionMessageHandler = viewer::MessageHandler;
using SessionState = viewer::SessionState;
using ViewerActions = viewer::ViewerActions;
using ViewerMode = viewer::ViewerMode;
using ViewerSession = viewer::ViewerSession;

/// <summary>
/// The tray and the viewer as a pair, over a real socket, with a real <see cref="InlineQueue"/> on
/// one side and a real <see cref="SessionState"/> on the other. Every other test in this repo holds
/// one half still — <see cref="OwnedInlineHostTest"/> drives the tray's queue with raw messages,
/// <see cref="TrackerSnapshotTest"/> drives the tray against a <see cref="FakeViewer"/> — so
/// nothing until now could catch the two agreeing on a verb but disagreeing on what the user is
/// left looking at.
/// <para>
/// Both ownership arrangements are covered, because which process owns the queue is decided by
/// whichever bound the port and the answer changes which code applies an accept:
/// </para>
/// <list type="bullet">
/// <item><b>Tray owned</b>: the usual case, since the tray starts at login. The viewer is
/// display only, polls <c>listfull</c>, and forwards every acting command.</item>
/// <item><b>Viewer owned</b>: a viewer bound the port before the tray started. The viewer applies
/// locally and the tray is the remote control.</item>
/// </list>
/// <para>
/// Tracked moves and deletes are only in the pair for the tray owned case, and deliberately: they
/// live in the tray, and a tray that does not own the queue has no way to publish them to the
/// process that does.
/// </para>
/// </summary>
public class TrayViewerSyncTest
{
    #region tray owned

    [Test]
    public async Task TrayAcceptAllEmptiesTheAttachedViewer()
    {
        await using var pair = new TrayOwned();
        pair.Queue(sample, 1);
        pair.Queue(other, 7);
        var move = pair.AddMove();
        var delete = pair.AddDelete();
        await Assert.That(pair.Pump().Keys()).IsEquivalentTo([Key(sample, 1), Key(other, 7), move.Key, delete.Key]);

        await pair.Tracker.AcceptAll();

        var viewer = pair.Pump();
        await Assert.That(viewer.Queue).IsEmpty();
        // Nothing left to show, and this window is not what holds the queue, so it closes itself.
        await Assert.That(viewer.Exit).IsTrue();
        await Assert.That(pair.Tracker.TrackingAny).IsFalse();
        await Assert.That(File.Exists(move.Target)).IsTrue();
        await Assert.That(File.Exists(delete.File)).IsFalse();
    }

    [Test]
    public async Task TrayAcceptOfOneSnapshotLeavesTheRestInTheAttachedViewer()
    {
        await using var pair = new TrayOwned();
        var snapshot = pair.Queue(sample, 1);
        pair.Queue(other, 7);
        pair.Pump();

        await pair.Tracker.Accept(snapshot);

        await Assert.That(pair.Pump().Keys()).IsEquivalentTo([Key(other, 7)]);
        await Assert.That(pair.Tracker.Snapshots.Select(_ => _.Key)).IsEquivalentTo([Key(other, 7)]);
    }

    [Test]
    public async Task TrayDiscardOfOneSnapshotReachesTheAttachedViewer()
    {
        await using var pair = new TrayOwned();
        var snapshot = pair.Queue(sample, 1);
        pair.Queue(other, 7);
        pair.Pump();

        pair.Tracker.Discard(snapshot);

        await Assert.That(pair.Pump().Keys()).IsEquivalentTo([Key(other, 7)]);
    }

    /// <summary>
    /// The tray menu's "Discard (n)", which counts snapshots and tracked files together and so has
    /// to sweep both.
    /// </summary>
    [Test]
    public async Task TrayDiscardAllEmptiesTheAttachedViewer()
    {
        await using var pair = new TrayOwned();
        pair.Queue(sample, 1);
        var move = pair.AddMove();
        pair.Pump();

        pair.Tracker.Clear();

        var viewer = pair.Pump();
        await Assert.That(viewer.Queue).IsEmpty();
        await Assert.That(viewer.Exit).IsTrue();
        // Discarding a move throws its received file away, wherever the discard came from.
        await Assert.That(File.Exists(move.Temp)).IsFalse();
        await Assert.That(File.Exists(move.Target)).IsFalse();
    }

    [Test]
    public async Task TrayAcceptOfATrackedMoveReachesTheAttachedViewer()
    {
        await using var pair = new TrayOwned();
        pair.Queue(sample, 1);
        var move = pair.AddMove();
        await Assert.That(pair.Pump().Keys()).IsEquivalentTo([Key(sample, 1), move.Key]);

        pair.Tracker.Accept(pair.Tracker.Moves.Single());

        await Assert.That(pair.Pump().Keys()).IsEquivalentTo([Key(sample, 1)]);
        await Assert.That(File.ReadAllText(move.Target)).IsEqualTo("received");
    }

    [Test]
    public async Task TrayAcceptOfATrackedDeleteReachesTheAttachedViewer()
    {
        await using var pair = new TrayOwned();
        pair.Queue(sample, 1);
        var delete = pair.AddDelete();
        pair.Pump();

        pair.Tracker.Accept(pair.Tracker.Deletes.Single());

        await Assert.That(pair.Pump().Keys()).IsEquivalentTo([Key(sample, 1)]);
        await Assert.That(File.Exists(delete.File)).IsFalse();
    }

    /// <summary>
    /// A passing re-run settles the entry wherever the queue lives, so the window stops offering a
    /// snapshot that is already in the source.
    /// </summary>
    [Test]
    public async Task ASettleReachesTheAttachedViewer()
    {
        await using var pair = new TrayOwned();
        pair.Queue(sample, 1);
        pair.Queue(other, 7);
        pair.Pump();

        pair.Send(new(ViewerVerb.Settle, Key(sample, 1)));

        await Assert.That(pair.Pump().Keys()).IsEquivalentTo([Key(other, 7)]);
        await Assert.That(pair.Tracker.Snapshots.Select(_ => _.Key)).IsEquivalentTo([Key(other, 7)]);
    }

    /// <summary>
    /// The attached viewer's "Accept all" sweeps what it was shown, which is the tray's tracked
    /// files as well as the snapshots.
    /// </summary>
    [Test]
    public async Task ViewerAcceptAllEmptiesTheTray()
    {
        await using var pair = new TrayOwned();
        pair.Queue(sample, 1);
        pair.Queue(other, 7);
        var move = pair.AddMove();
        var delete = pair.AddDelete();
        pair.Pump();

        pair.Link.Post(ViewerSideVerb.AcceptAll, null);

        var viewer = pair.Pump();
        await Assert.That(viewer.Queue).IsEmpty();
        await Assert.That(pair.Tracker.Snapshots).IsEmpty();
        await Assert.That(pair.Tracker.Moves).IsEmpty();
        await Assert.That(pair.Tracker.Deletes).IsEmpty();
        await Assert.That(File.ReadAllText(move.Target)).IsEqualTo("received");
        await Assert.That(File.Exists(delete.File)).IsFalse();
    }

    [Test]
    public async Task ViewerAcceptOfOneSnapshotReachesTheTray()
    {
        await using var pair = new TrayOwned();
        pair.Queue(sample, 1);
        pair.Queue(other, 7);
        pair.Pump();

        pair.Link.Post(ViewerSideVerb.Accept, Key(sample, 1));

        await Assert.That(pair.Pump().Keys()).IsEquivalentTo([Key(other, 7)]);
        await Assert.That(pair.Tracker.Snapshots.Select(_ => _.Key)).IsEquivalentTo([Key(other, 7)]);
        await Assert.That(pair.Applied.Select(_ => _.LineHint)).IsEquivalentTo([1]);
    }

    [Test]
    public async Task ViewerDiscardOfOneSnapshotReachesTheTray()
    {
        await using var pair = new TrayOwned();
        pair.Queue(sample, 1);
        pair.Queue(other, 7);
        pair.Pump();

        pair.Link.Post(ViewerSideVerb.Discard, Key(sample, 1));

        await Assert.That(pair.Pump().Keys()).IsEquivalentTo([Key(other, 7)]);
        await Assert.That(pair.Tracker.Snapshots.Select(_ => _.Key)).IsEquivalentTo([Key(other, 7)]);
        await Assert.That(pair.Applied).IsEmpty();
    }

    [Test]
    public async Task ViewerDiscardAllEmptiesTheTray()
    {
        await using var pair = new TrayOwned();
        pair.Queue(sample, 1);
        var move = pair.AddMove();
        var delete = pair.AddDelete();
        pair.Pump();

        pair.Link.Post(ViewerSideVerb.DiscardAll, null);

        await Assert.That(pair.Pump().Queue).IsEmpty();
        await Assert.That(pair.Tracker.TrackingAny).IsFalse();
        await Assert.That(File.Exists(move.Temp)).IsFalse();
        // Discarding a pending delete has always meant leaving the file; the next run re-tracks it.
        await Assert.That(File.Exists(delete.File)).IsTrue();
    }

    [Test]
    public async Task ViewerAcceptOfATrackedMoveReachesTheTray()
    {
        await using var pair = new TrayOwned();
        pair.Queue(sample, 1);
        var move = pair.AddMove();
        pair.Pump();

        pair.Link.Post(ViewerSideVerb.Accept, move.Key);

        await Assert.That(pair.Pump().Keys()).IsEquivalentTo([Key(sample, 1)]);
        await Assert.That(pair.Tracker.Moves).IsEmpty();
        await Assert.That(File.ReadAllText(move.Target)).IsEqualTo("received");
    }

    /// <summary>
    /// A failed apply keeps its entry so it can be retried, and both surfaces have to say the same
    /// thing about it — the tray in a balloon, the viewer in the entry's status.
    /// </summary>
    [Test]
    public async Task AFailedAcceptStaysPendingOnBothSides()
    {
        await using var pair = new TrayOwned(_ => InlineApplyResult.Failed("the file is locked"));
        var snapshot = pair.Queue(sample, 1);
        pair.Pump();

        await pair.Tracker.Accept(snapshot);

        var viewer = pair.Pump();
        await Assert.That(viewer.Queue.Single().Status).IsEqualTo("the file is locked");
        await Assert.That(viewer.Exit).IsFalse();
        await Assert.That(pair.Tracker.Snapshots.Single().Status).IsEqualTo("the file is locked");
        await Assert.That(pair.Failures.Single()).Contains("the file is locked");
    }

    /// <summary>
    /// A bulk accept that could not apply everything must not report success on either side: what
    /// failed stays pending, the window keeps showing it, and the tray says so rather than leaving
    /// the menu to offer it again a scan later.
    /// </summary>
    [Test]
    public async Task TrayAcceptAllReportsWhatStayedPending()
    {
        await using var pair = new TrayOwned(
            patch => patch.SourceFile == sample
                ? InlineApplyResult.Failed("the file is locked")
                : InlineApplyResult.Applied);
        pair.Queue(sample, 1);
        pair.Queue(other, 7);
        pair.Pump();

        await pair.Tracker.AcceptAll();

        var viewer = pair.Pump();
        await Assert.That(viewer.Keys()).IsEquivalentTo([Key(sample, 1)]);
        await Assert.That(viewer.Queue.Single().Status).IsEqualTo("the file is locked");
        await Assert.That(pair.Tracker.Snapshots.Single().Status).IsEqualTo("the file is locked");
        await Assert.That(pair.Failures.Single()).Contains("the file is locked");
    }

    /// <summary>
    /// Neither surface picks a side of a conflict silently, so a bulk accept from either leaves the
    /// entry exactly where the other one would have.
    /// </summary>
    [Test]
    public async Task AConflictSurvivesABulkAcceptFromEitherSide()
    {
        await using var pair = new TrayOwned();
        pair.Queue(sample, 1, "eight", "net8.0");
        pair.Queue(sample, 1, "nine", "net9.0");
        pair.Queue(other, 7);
        pair.Pump();

        await pair.Tracker.AcceptAll();
        await Assert.That(pair.Pump().Keys()).IsEquivalentTo([Key(sample, 1)]);

        pair.Link.Post(ViewerSideVerb.AcceptAll, null);

        var viewer = pair.Pump();
        await Assert.That(viewer.Keys()).IsEquivalentTo([Key(sample, 1)]);
        await Assert.That(viewer.Queue.Single().Conflicted).IsTrue();
        await Assert.That(pair.Applied.Select(_ => _.NewContent)).IsEquivalentTo(["new"]);
    }

    #endregion

    #region viewer owned

    [Test]
    public async Task TrayAcceptAllEmptiesTheOwningViewer()
    {
        await using var pair = new ViewerOwned();
        pair.Queue(sample, 1);
        pair.Queue(other, 7);
        await Assert.That(pair.Viewer.Keys()).IsEquivalentTo([Key(sample, 1), Key(other, 7)]);

        await pair.Tracker.AcceptAll();

        await Assert.That(pair.Viewer.Queue).IsEmpty();
        await Assert.That(pair.Tracker.Snapshots).IsEmpty();
        await Assert.That(pair.Applied.Count).IsEqualTo(2);
    }

    [Test]
    public async Task TrayAcceptOfOneSnapshotLeavesTheRestInTheOwningViewer()
    {
        await using var pair = new ViewerOwned();
        var snapshot = pair.Snapshot(sample, 1);
        pair.Queue(other, 7);

        await pair.Tracker.Accept(snapshot);

        await Assert.That(pair.Viewer.Keys()).IsEquivalentTo([Key(other, 7)]);
        await Assert.That(pair.Tracker.Snapshots.Select(_ => _.Key)).IsEquivalentTo([Key(other, 7)]);
    }

    [Test]
    public async Task TrayDiscardReachesTheOwningViewer()
    {
        await using var pair = new ViewerOwned();
        var snapshot = pair.Snapshot(sample, 1);
        pair.Queue(other, 7);

        pair.Tracker.Discard(snapshot);

        await Assert.That(pair.Viewer.Keys()).IsEquivalentTo([Key(other, 7)]);
        await Assert.That(pair.Applied).IsEmpty();
    }

    [Test]
    public async Task TrayDiscardAllEmptiesTheOwningViewer()
    {
        await using var pair = new ViewerOwned();
        pair.Queue(sample, 1);
        pair.Queue(other, 7);

        pair.Tracker.Clear();

        await Assert.That(pair.Viewer.Queue).IsEmpty();
        await Assert.That(pair.Tracker.Snapshots).IsEmpty();
        await Assert.That(pair.Applied).IsEmpty();
    }

    [Test]
    public async Task ViewerAcceptReachesTheTrayListing()
    {
        await using var pair = new ViewerOwned();
        pair.Queue(sample, 1);
        pair.Queue(other, 7);

        pair.Act(CommandKind.Accept, Key(sample, 1));

        await Assert.That(pair.Viewer.Keys()).IsEquivalentTo([Key(other, 7)]);
        await Assert.That(pair.Tracker.Snapshots.Select(_ => _.Key)).IsEquivalentTo([Key(other, 7)]);
    }

    [Test]
    public async Task ViewerAcceptAllEmptiesTheTrayListing()
    {
        await using var pair = new ViewerOwned();
        pair.Queue(sample, 1);
        pair.Queue(other, 7);

        pair.Act(CommandKind.AcceptAll, null);

        await Assert.That(pair.Viewer.Queue).IsEmpty();
        await Assert.That(pair.Tracker.Snapshots).IsEmpty();
        await Assert.That(pair.Tracker.TrackingAny).IsFalse();
    }

    [Test]
    public async Task ViewerDiscardReachesTheTrayListing()
    {
        await using var pair = new ViewerOwned();
        pair.Queue(sample, 1);
        pair.Queue(other, 7);

        pair.Act(CommandKind.Discard, Key(sample, 1));

        await Assert.That(pair.Viewer.Keys()).IsEquivalentTo([Key(other, 7)]);
        await Assert.That(pair.Tracker.Snapshots.Select(_ => _.Key)).IsEquivalentTo([Key(other, 7)]);
        await Assert.That(pair.Applied).IsEmpty();
    }

    /// <summary>
    /// Same as <see cref="AFailedAcceptStaysPendingOnBothSides"/>, with the queue on the other side
    /// of the socket: the tray reads the failure off the wire rather than out of its own queue.
    /// </summary>
    [Test]
    public async Task AFailedAcceptStaysPendingOnBothSidesOfAnOwningViewer()
    {
        await using var pair = new ViewerOwned(_ => ViewerSideApplyResult.Failed("the file is locked"));
        var snapshot = pair.Snapshot(sample, 1);

        await pair.Tracker.Accept(snapshot);

        await Assert.That(pair.Viewer.Queue.Single().Status).IsEqualTo("the file is locked");
        await Assert.That(pair.Tracker.Snapshots.Single().Status).IsEqualTo("the file is locked");
        await Assert.That(pair.Failures.Single()).Contains("the file is locked");
    }

    /// <inheritdoc cref="TrayAcceptAllReportsWhatStayedPending"/>
    [Test]
    public async Task TrayAcceptAllReportsWhatTheOwningViewerKept()
    {
        await using var pair = new ViewerOwned(
            patch => patch.SourceFile == sample
                ? ViewerSideApplyResult.Failed("the file is locked")
                : ViewerSideApplyResult.Applied);
        pair.Queue(sample, 1);
        pair.Queue(other, 7);

        await pair.Tracker.AcceptAll();

        await Assert.That(pair.Viewer.Keys()).IsEquivalentTo([Key(sample, 1)]);
        await Assert.That(pair.Viewer.Queue.Single().Status).IsEqualTo("the file is locked");
        await Assert.That(pair.Tracker.Snapshots.Single().Status).IsEqualTo("the file is locked");
        await Assert.That(pair.Failures.Single()).Contains("the file is locked");
    }

    [Test]
    public async Task AConflictIsRefusedTheSameWayFromTheTray()
    {
        await using var pair = new ViewerOwned();
        pair.Queue(sample, 1, "eight", "net8.0");
        var snapshot = pair.Snapshot(sample, 1, "nine", "net9.0");

        await pair.Tracker.Accept(snapshot);

        await Assert.That(pair.Viewer.Queue.Single().Conflicted).IsTrue();
        await Assert.That(pair.Applied).IsEmpty();
        await Assert.That(pair.Failures.Single()).Contains("Conflicting snapshots (net8.0 / net9.0), resolve in the viewer");
    }

    #endregion

    #region no tray running

    /// <summary>
    /// The whole point of the delete verb. With no tray, a stale verified file used to be reported
    /// to nothing at all: DiffEngine skipped the send outright, and a delete has no second file to
    /// compare against, so no diff tool ever opened for it either.
    /// <para>
    /// End to end from the public API: DiffEngine, over a real socket, into a real session, onto
    /// disk. Nothing is launched here because this viewer already owns the port, which is the same
    /// branch a second failing test takes.
    /// </para>
    /// </summary>
    [Test]
    public async Task ADeleteWithNoTrayReachesTheViewerAndTheFileGoes()
    {
        await using var pair = new ViewerOwned();
        using var noTray = new NoTray();
        var file = pair.StageStaleFile();

        DiffRunner.AddDelete(file);

        await Assert.That(pair.Viewer.Keys()).IsEquivalentTo([TrackedKeys.ForDelete(file)]);

        pair.Act(CommandKind.Accept, TrackedKeys.ForDelete(file));

        await Assert.That(File.Exists(file)).IsFalse();
        await Assert.That(pair.Viewer.Queue).IsEmpty();
    }

    [Test]
    public async Task ADiscardedDeleteWithNoTrayLeavesTheFile()
    {
        await using var pair = new ViewerOwned();
        using var noTray = new NoTray();
        var file = pair.StageStaleFile();
        await DiffRunner.AddDeleteAsync(file);

        pair.Act(CommandKind.Discard, TrackedKeys.ForDelete(file));

        await Assert.That(File.Exists(file)).IsTrue();
        await Assert.That(pair.Viewer.Queue).IsEmpty();
    }

    /// <summary>
    /// A move rides along in a window that is already open rather than starting one: DiffRunner has
    /// already opened a diff tool for that file pair, and a second window competing with it is not
    /// an improvement.
    /// </summary>
    [Test]
    public async Task AMoveWithNoTrayReachesTheViewerAndTheFileMoves()
    {
        await using var pair = new ViewerOwned();
        using var noTray = new NoTray();
        var move = pair.StageMove();

        PendingFiles.AddMove(move.Temp, move.Target, null, null, false, null);

        await Assert.That(pair.Viewer.Keys()).IsEquivalentTo([move.Key]);

        pair.Act(CommandKind.Accept, move.Key);

        await Assert.That(File.ReadAllText(move.Target)).IsEqualTo("received");
        await Assert.That(File.Exists(move.Temp)).IsFalse();
    }

    [Test]
    public async Task ADiscardedMoveWithNoTrayThrowsTheReceivedFileAway()
    {
        await using var pair = new ViewerOwned();
        using var noTray = new NoTray();
        var move = pair.StageMove();
        await PendingFiles.AddMoveAsync(move.Temp, move.Target, null, null, false, null, Cancel.None);

        pair.Act(CommandKind.Discard, move.Key);

        await Assert.That(File.Exists(move.Temp)).IsFalse();
        await Assert.That(File.Exists(move.Target)).IsFalse();
    }

    /// <summary>
    /// A re-run stages the same received file again, and one entry is what a reviewer should see.
    /// </summary>
    [Test]
    public async Task AResentMoveReplacesItsEntry()
    {
        await using var pair = new ViewerOwned();
        using var noTray = new NoTray();
        var move = pair.StageMove();
        PendingFiles.AddMove(move.Temp, move.Target, null, null, false, null);

        File.WriteAllText(move.Temp, "second run");
        PendingFiles.AddMove(move.Temp, move.Target, null, null, false, null);

        await Assert.That(pair.Viewer.Queue).HasSingleItem();
        await Assert.That(pair.Viewer.Queue.Single().LeftText).IsEqualTo("second run");
    }

    /// <summary>
    /// Whoever holds the pending files lists them, so a second viewer attaching to this one shows
    /// what it is holding — the same answer a tray owner gives for its own.
    /// </summary>
    [Test]
    public async Task AFullListingCarriesTheViewersOwnPendingFiles()
    {
        await using var pair = new ViewerOwned();
        using var noTray = new NoTray();
        var file = pair.StageStaleFile();
        DiffRunner.AddDelete(file);

        var full = pair.Send(new(ViewerVerb.ListFull));
        var plain = pair.Send(new(ViewerVerb.List));

        await Assert.That(full.Deletes.Single().File).IsEqualTo(file);
        // The plain listing drives the tray menu, which reads its own tracker for these.
        await Assert.That(plain.Deletes).IsEmpty();
    }

    /// <summary>
    /// A tray that owns the queue answers these too, and routes them into the same tracked files
    /// the piper port fills. That is not theoretical: a test process that started before the tray
    /// has its tray check cached false for good, so its pending files arrive this way for the rest
    /// of its life, and were dropped before.
    /// </summary>
    [Test]
    public async Task AnOwningTrayTracksWhatArrivesOnTheViewerPort()
    {
        await using var pair = new TrayOwned();
        var delete = pair.AddStaleFile();

        pair.Send(new(ViewerVerb.Delete, delete));

        await Assert.That(pair.Tracker.Deletes.Select(_ => _.File)).IsEquivalentTo([delete]);
        await Assert.That(pair.Pump().Keys()).IsEquivalentTo([TrackedKeys.ForDelete(delete)]);
    }

    /// <summary>
    /// The tray check is cached, so this is the state a test process is in, not a property of the
    /// machine. Set explicitly rather than assumed, because another test in this project sets it
    /// true.
    /// </summary>
#pragma warning disable CS0618 // DiffEngineTray is the obsolete shim, but its IsRunning is still where the tray check lives.
    sealed class NoTray : IDisposable
    {
        readonly bool previousTray = DiffEngine.DiffEngineTray.IsRunning;
        readonly bool previousDisabled = DiffRunner.Disabled;

        public NoTray()
        {
            DiffEngine.DiffEngineTray.IsRunning = false;
            // DisabledChecker turns this on for build servers, and these tests drive the real
            // DiffRunner entry points.
            DiffRunner.Disabled = false;
        }

        public void Dispose()
        {
            DiffEngine.DiffEngineTray.IsRunning = previousTray;
            DiffRunner.Disabled = previousDisabled;
        }
    }
#pragma warning restore CS0618

    #endregion

    const string sample = @"c:\repo\SampleTests.cs";
    const string other = @"c:\repo\OtherTests.cs";

    static string Key(string source, int line) =>
        InlineKey.For(source, line);

    static string Payload(string source, int line, string content, string? framework) =>
        InlinePatchFile.Build(
            new(source, line, "\"old\"", content)
            {
                Framework = framework,
                TestName = null
            });

    /// <summary>
    /// The temp directory a pair stages its tracked files in, plus the paths a test asserts over.
    /// </summary>
    record TrackedMoveFiles(string Key, string Temp, string Target);

    record TrackedDeleteFile(string Key, string File);

    /// <summary>
    /// The usual arrangement: this tray bound the port, holds the queue, and a display only viewer
    /// polls it. Nothing is launched — <see cref="FakeLauncher"/> stands in for the process the
    /// host would otherwise start, and <see cref="Link"/> is the window it stands for.
    /// </summary>
    sealed class TrayOwned : IAsyncDisposable
    {
        public TrayOwned(Func<InlinePatch, InlineApplyResult>? applier = null)
        {
            Host = OwnedInlineHost.TryOwn(
                       Warnings.Add,
                       new FakeLauncher(),
                       0,
                       patch =>
                       {
                           Applied.Add(patch);
                           return applier?.Invoke(patch) ?? InlineApplyResult.Applied;
                       }) ??
                   throw new("Could not bind an ephemeral port.");
            Tracker = new(inlineFailed: Failures.Add, inline: Host);
            // Wired the way Program does, and before serving starts: a queue change arriving over
            // the socket has to reach the listing the tray menu and the icon read, not wait for the
            // next two second scan.
            Host.Changed = Tracker.Refresh;
            Host.TrackedFiles = Tracker;
            Host.Start();
            Window = new(SessionState.Start(ViewerMode.Inline));
            Link = new(Window, Host.Port);
            Directory.CreateDirectory(root);
        }

        public OwnedInlineHost Host { get; }
        public RecordingTracker Tracker { get; }
        public SessionHost Window { get; }
        public OwnerLink Link { get; }
        public List<InlinePatch> Applied { get; } = [];
        public List<string> Warnings { get; } = [];
        public List<string> Failures { get; } = [];

        readonly string root = TempRoot();

        /// <summary>
        /// One turn of the attached viewer's polling thread: post whatever the window queued, then
        /// read the owner's queue back into the session.
        /// </summary>
        public SessionState Pump()
        {
            if (!Link.Pump())
            {
                throw new("The queue owner did not answer.");
            }

            return Window.State;
        }

        public ViewerResponse Send(ViewerMessage message)
        {
            if (!ViewerClient.TrySend(message, out var response, Host.Port))
            {
                throw new($"No response for {message.Verb}.");
            }

            return response;
        }

        /// <summary>
        /// A failing inline snapshot arriving from a test process.
        /// </summary>
        public PendingSnapshot Queue(string source, int line, string content = "new", string? framework = null)
        {
            var response = Send(new(ViewerVerb.Inline, Body: Payload(source, line, content, framework)));
            if (!response.Ok)
            {
                throw new($"The owner refused the patch. {response.Message}");
            }

            return Tracker.Snapshots.Single(_ => _.Key == Key(source, line));
        }

        public TrackedMoveFiles AddMove()
        {
            // Its own directory, the way DiffEngine stages received files, because accepting a move
            // deletes that directory.
            var directory = Path.Combine(root, $"move_{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var temp = Path.Combine(directory, "Sample.Test.received.txt");
            File.WriteAllText(temp, "received");
            var target = Path.Combine(root, $"Sample.Test.{Guid.NewGuid():N}.verified.txt");
            Tracker.AddMove(temp, target, null, null, false, null);
            return new(TrackedKeys.ForMove(temp), temp, target);
        }

        public TrackedDeleteFile AddDelete()
        {
            var file = AddStaleFile();
            Tracker.AddDelete(file);
            return new(TrackedKeys.ForDelete(file), file);
        }

        /// <summary>
        /// Staged but not tracked, for the paths that arrive over a socket rather than being added
        /// to the tracker directly.
        /// </summary>
        public string AddStaleFile()
        {
            var file = Path.Combine(root, $"Stale.{Guid.NewGuid():N}.verified.txt");
            File.WriteAllText(file, "stale");
            return file;
        }

        public async ValueTask DisposeAsync()
        {
            await Tracker.DisposeAsync();
            await Host.DisposeAsync();
            FileEx.SafeDeleteDirectory(root);
        }
    }

    /// <summary>
    /// A viewer that bound the port before this tray started, so it owns the queue and applies
    /// locally while the tray drives it over the wire.
    /// <para>
    /// Also the shape a machine with no tray installed is in, which is why this harness holds the
    /// pending files too: DiffEngine addresses the queue owner when no tray answered its startup
    /// check, so the moves and deletes land here rather than in a tracker.
    /// </para>
    /// </summary>
    sealed class ViewerOwned : IAsyncDisposable
    {
        public ViewerOwned(Func<ViewerSidePatch, ViewerSideApplyResult>? applier = null)
        {
            if (!ViewerSideServer.TryBind(0, out var bound))
            {
                throw new("Could not bind an ephemeral port.");
            }

            server = bound;
            // Points the tray's RemoteInlineHost, and DiffEngine's own sends, here — and keeps a
            // viewer that happens to be running on this machine out of the way.
            previousPort = Environment.GetEnvironmentVariable(ViewerClient.PortVariable);
            Environment.SetEnvironmentVariable(ViewerClient.PortVariable, server.Port.ToString());
            Window = new(SessionState.Start(ViewerMode.Inline));
            actions = new ViewerActions(
                patch =>
                {
                    Applied.Add(patch);
                    return applier?.Invoke(patch) ?? ViewerSideApplyResult.Applied;
                },
                (_, _) => throw new("A queued snapshot is never accepted by copying a file."),
                _ =>
                {
                })
            {
                // The real ones, so accepting a pending file here is the file operation itself
                // rather than a recording of one.
                MoveFile = ViewerActions.Real.MoveFile,
                DeleteFile = ViewerActions.Real.DeleteFile
            };
            var handler = new SessionMessageHandler(Window, actions, Windows.Enqueue);
            listening = server.Listen(handler.Handle, cancel.Token);
            Tracker = new(inlineFailed: Failures.Add);
            Directory.CreateDirectory(root);
        }

        readonly ViewerSideServer server;
        readonly CancelSource cancel = new();
        readonly Task listening;
        readonly string? previousPort;
        readonly ViewerActions actions;
        readonly string root = TempRoot();

        /// <summary>
        /// A verified file a passing test no longer produces, which is what DiffEngine reports as
        /// a pending delete.
        /// </summary>
        public string StageStaleFile()
        {
            var file = Path.Combine(root, $"Stale.{Guid.NewGuid():N}.verified.txt");
            File.WriteAllText(file, "stale");
            return file;
        }

        public TrackedMoveFiles StageMove()
        {
            var directory = Path.Combine(root, $"move_{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var temp = Path.Combine(directory, "Sample.Test.received.txt");
            File.WriteAllText(temp, "received");
            var target = Path.Combine(root, $"Sample.Test.{Guid.NewGuid():N}.verified.txt");
            return new(TrackedKeys.ForMove(temp), temp, target);
        }

        public SessionHost Window { get; }
        public RecordingTracker Tracker { get; }
        public List<ViewerSidePatch> Applied { get; } = [];
        public List<string> Failures { get; } = [];
        public ConcurrentQueue<ViewerSideWindowCommand> Windows { get; } = new();

        public SessionState Viewer => Window.State;

        public void Queue(string source, int line, string content = "new", string? framework = null)
        {
            var message = new ViewerMessage(ViewerVerb.Inline, Body: Payload(source, line, content, framework));
            if (!ViewerClient.TrySend(message, out var response, server.Port) ||
                !response.Ok)
            {
                throw new($"The owner refused the patch. {response?.Message}");
            }
        }

        public PendingSnapshot Snapshot(string source, int line, string content = "new", string? framework = null)
        {
            Queue(source, line, content, framework);
            return Tracker.Snapshots.Single(_ => _.Key == Key(source, line));
        }

        public ViewerResponse Send(ViewerMessage message)
        {
            if (!ViewerClient.TrySend(message, out var response, server.Port))
            {
                throw new($"No response for {message.Verb}.");
            }

            return response;
        }

        /// <summary>
        /// What the window does with a command when it owns the queue: applies it here, selecting
        /// the entry first the way every acting path does. The forwarding branch is the other
        /// arrangement, and <see cref="TrayOwned.Link"/> covers it.
        /// </summary>
        public void Act(CommandKind command, string? key) =>
            Window.Mutate(state =>
            {
                if (key is not null)
                {
                    state = ViewerSession.SelectKey(state, key);
                }

                return ViewerSession.Apply(state, command, actions);
            });

        public async ValueTask DisposeAsync()
        {
            await Tracker.DisposeAsync();
            await cancel.CancelAsync();
            server.Dispose();
            try
            {
                await listening.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception exception)
                when (exception is OperationCanceledException or TimeoutException)
            {
                // Cancellation unwinds through the listener; nothing to report.
            }

            cancel.Dispose();
            Environment.SetEnvironmentVariable(ViewerClient.PortVariable, previousPort);
            FileEx.SafeDeleteDirectory(root);
        }
    }

    static string TempRoot() =>
        Path.Combine(Path.GetTempPath(), $"TrayViewerSync_{Guid.NewGuid():N}");
}

static class TrayViewerSyncExtensions
{
    /// <summary>
    /// What the window is showing, in display order, as the keys both sides address entries by.
    /// </summary>
    public static IReadOnlyList<string> Keys(this SessionState state) =>
        state.Queue.Select(_ => _.Key).ToList();
}
