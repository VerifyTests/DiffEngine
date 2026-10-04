// DiffEngineTray is the obsolete public shim, but its IsRunning is still where the tray check
// lives, and these tests have to hold it down.
#pragma warning disable CS0618

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// The route a pair takes when the diff tool resolved for it is the viewer itself: queued with
/// whoever owns the queue rather than given a process and a window of its own.
/// <para>
/// Driven over a real socket against a server that only records, so what is asserted is the wire
/// - which is all DiffEngine controls. What an owner then does with a <c>Diff</c> is the viewer's
/// half and is covered where the viewer's own handler is.
/// </para>
/// </summary>
[NotInParallel]
public class PendingFilesDiffTests
{
    [Test]
    public async Task ADiffReachesTheOwnerWithBothPaths()
    {
        using var owner = new Recording();

        var result = await PendingFiles.AddDiffAsync(Viewer(), temp, target, Cancel.None);

        await Assert.That(result).IsEqualTo(LaunchResult.AlreadyRunningAndSupportsRefresh);
        await Assert.That(owner.Heard).IsEquivalentTo([$"{ViewerVerb.Diff}:{temp}:{target}"]);
    }

    [Test]
    public async Task ASyncDiffReachesTheOwnerToo()
    {
        using var owner = new Recording();

        var result = PendingFiles.AddDiff(Viewer(), temp, target);

        await Assert.That(result).IsEqualTo(LaunchResult.AlreadyRunningAndSupportsRefresh);
        await Assert.That(owner.Heard).IsEquivalentTo([$"{ViewerVerb.Diff}:{temp}:{target}"]);
    }

    /// <summary>
    /// An owner that answers and says no is one too old to know the verb. Launching a second
    /// viewer cannot change that answer and would bind nothing, so the pair goes over as a plain
    /// move: a row with nothing raised over it, which every owner has always understood.
    /// </summary>
    [Test]
    public async Task ARefusedDiffFallsBackToAPlainMove()
    {
        using var owner = new Recording {Refuse = ViewerVerb.Diff};

        var result = await PendingFiles.AddDiffAsync(Viewer(), temp, target, Cancel.None);

        await Assert.That(result).IsEqualTo(LaunchResult.AlreadyRunningAndSupportsRefresh);
        await Assert.That(owner.Heard).IsEquivalentTo(
        [
            $"{ViewerVerb.Diff}:{temp}:{target}",
            $"{ViewerVerb.Move}:{temp}:{target}"
        ]);
    }

    [Test]
    public async Task ASyncRefusedDiffFallsBackToAPlainMove()
    {
        using var owner = new Recording {Refuse = ViewerVerb.Diff};

        PendingFiles.AddDiff(Viewer(), temp, target);

        await Assert.That(owner.Heard).IsEquivalentTo(
        [
            $"{ViewerVerb.Diff}:{temp}:{target}",
            $"{ViewerVerb.Move}:{temp}:{target}"
        ]);
    }

    /// <summary>
    /// With nothing owning the queue this route starts a viewer, and MaxInstancesToLaunch(0) says
    /// no window opens. It used to be exempt on the grounds that the viewer queues rather than
    /// opening one per pair - true of every pair after the first, and not of the first, which
    /// starts a process.
    /// <para>
    /// This is the arrangement a test suite that has to leave diff on runs in: no tray, no owner,
    /// and the cap at zero. Before, every staged snapshot in such a run put a viewer on the
    /// screen, and nothing in DiffEngine could be set to stop it.
    /// </para>
    /// </summary>
    [Test]
    public async Task WithNoOwnerAndNoSlotNothingIsStarted()
    {
        using var absent = new NoOwner();

        DiffRunner.MaxInstancesToLaunch(0);
        MaxInstance.ResetCount();
        try
        {
            var result = await PendingFiles.AddDiffAsync(Viewer(), temp, target, Cancel.None);

            await Assert.That(result).IsEqualTo(LaunchResult.TooManyRunningDiffTools);
        }
        finally
        {
            MaxInstance.ResetAppDomainValue();
            MaxInstance.ResetCount();
        }
    }

    /// <summary>
    /// The other end: the pair's test started passing, so the row it took goes. A settle rather
    /// than a kill, because there is no process of its own to kill, and rather than a discard,
    /// because the received file a discard would delete is one DiffEngine has already removed.
    /// </summary>
    [Test]
    public async Task SettlingSendsTheMoveKey()
    {
        using var owner = new Recording();

        PendingFiles.SettleDiff(temp);

        await Assert.That(owner.Heard).IsEquivalentTo([$"{ViewerVerb.Settle}:{TrackedKeys.ForMove(temp)}:"]);
    }

    /// <summary>
    /// Nobody owning the queue means no row to drop, which is the goal state already. Silent
    /// rather than reported, the same bargain a pending delete with no surface makes.
    /// </summary>
    [Test]
    public async Task SettlingWithNoOwnerIsSilent()
    {
        using var absent = new NoOwner();

        await Assert.That(() => PendingFiles.SettleDiff(temp)).ThrowsNothing();
    }

    /// <summary>
    /// The other end of a delete: the file it was raised for is in use again, because a later run
    /// verified against it. The delete goes, by the tracked key it is listed under, and nothing
    /// reaches the file.
    /// </summary>
    [Test]
    public async Task SettlingADeleteSendsTheDeleteKey()
    {
        using var owner = new Recording();
        var previousDisabled = DiffRunner.Disabled;
        // DisabledChecker turns this on for build servers and AI CLIs, and this drives the real
        // DiffRunner entry point
        DiffRunner.Disabled = false;
        try
        {
            DiffRunner.SettleDelete(stale);
        }
        finally
        {
            DiffRunner.Disabled = previousDisabled;
        }

        await Assert.That(owner.Heard).IsEquivalentTo([$"{ViewerVerb.Settle}:{TrackedKeys.ForDelete(stale)}:"]);
    }

    /// <summary>
    /// A settle answers to the same switch the delete it settles did.
    /// </summary>
    [Test]
    public async Task SettlingADeleteWhileDisabledSendsNothing()
    {
        using var owner = new Recording();
        var previousDisabled = DiffRunner.Disabled;
        DiffRunner.Disabled = true;
        try
        {
            DiffRunner.SettleDelete(stale);
        }
        finally
        {
            DiffRunner.Disabled = previousDisabled;
        }

        await Assert.That(owner.Heard).IsEmpty();
    }

    [Test]
    public async Task SettlingADeleteWithNoOwnerIsSilent()
    {
        using var absent = new NoOwner();

        await Assert.That(() => PendingFiles.SettleDelete(stale)).ThrowsNothing();
    }

    /// <summary>
    /// The tray works the arguments out for itself when a move arrives without them, and used to
    /// take the viewer's declared ones - two plain paths, which open a window of its own for a
    /// pair whose queue is already on screen. Both callers ask this instead.
    /// </summary>
    [Test]
    public async Task AViewerIsReopenedIntoItsQueueAndNeverKilled()
    {
        var (arguments, canKill) = PendingFiles.RelaunchFor(Viewer(), temp, target);

        await Assert.That(arguments).IsEqualTo($"--diff \"{temp}\" \"{target}\"");
        // One window holds every pending pair, so killing it takes the rest with it.
        await Assert.That(canKill).IsFalse();
    }

    [Test]
    public async Task AnOrdinaryToolKeepsItsOwnArgumentsAndStaysKillable()
    {
        var (arguments, canKill) = PendingFiles.RelaunchFor(Other(isMdi: false), temp, target);

        await Assert.That(arguments).IsEqualTo($"\"{temp}\" \"{target}\"");
        await Assert.That(canKill).IsTrue();
    }

    [Test]
    public async Task AnMdiToolIsNotKillableEither()
    {
        var (_, canKill) = PendingFiles.RelaunchFor(Other(isMdi: true), temp, target);

        await Assert.That(canKill).IsFalse();
    }

    /// <summary>
    /// A page of a document the viewer is drawing is on screen already, in that document, so it is
    /// tracked with what it was derived from and nothing is opened for it. Not even resolved: the
    /// tool handed in here throws if it is asked for, and a cap of zero would refuse a launch.
    /// </summary>
    [Test]
    public async Task AFileDerivedFromADocumentTheViewerDrawsIsTrackedAndNothingIsOpened()
    {
        using var owner = new Recording();
        using var enabled = new Enabled();
        DiffRunner.MaxInstancesToLaunch(0);
        MaxInstance.ResetCount();
        try
        {
            var result = await DiffRunner.InnerLaunchAsync(
                NeverResolved,
                page,
                pageTarget,
                null,
                document,
                Resolves(Viewer(documents: true)));

            await Assert.That(result).IsEqualTo(LaunchResult.AlreadyRunningAndSupportsRefresh);
        }
        finally
        {
            MaxInstance.ResetAppDomainValue();
            MaxInstance.ResetCount();
        }

        await Assert.That(owner.Heard).IsEquivalentTo([$"{ViewerVerb.Move}:{page}:{pageTarget} from {document}"]);
    }

    [Test]
    public async Task ASyncDerivedFileIsTrackedTheSameWay()
    {
        using var owner = new Recording();
        using var enabled = new Enabled();

        // ReSharper disable once MethodHasAsyncOverload
        var result = DiffRunner.InnerLaunch(
            NeverResolved,
            page,
            pageTarget,
            null,
            document,
            Resolves(Viewer(documents: true)));

        await Assert.That(result).IsEqualTo(LaunchResult.AlreadyRunningAndSupportsRefresh);
        await Assert.That(owner.Heard).IsEquivalentTo([$"{ViewerVerb.Move}:{page}:{pageTarget} from {document}"]);
    }

    /// <summary>
    /// Nobody answering means the document is in no viewer either, since its own send went first
    /// and waited for one. So the page is opened as any pair is, which here finds no tool for it.
    /// What matters is that it was asked: tracked beneath a document nobody is showing, it would
    /// be a pending file with no window and no row.
    /// </summary>
    [Test]
    public async Task WithNobodyShowingTheDocumentADerivedFileIsOpenedAsAnyOther()
    {
        using var absent = new NoOwner();
        using var enabled = new Enabled();

        var result = await DiffRunner.InnerLaunchAsync(
            NoTool,
            page,
            pageTarget,
            null,
            document,
            Resolves(Viewer(documents: true)));

        await Assert.That(result).IsEqualTo(LaunchResult.NoDiffToolFound);
    }

    /// <summary>
    /// A document that went to Word, or Beyond Compare, or anything that is not a viewer drawing
    /// it, leaves its pages to be opened as they always were. What they were derived from is
    /// still said, to whoever tracks them.
    /// </summary>
    [Test]
    public async Task ADocumentInAnotherToolLeavesItsDerivedFilesToTheirOwnTools()
    {
        using var owner = new Recording();
        using var enabled = new Enabled();

        var result = await DiffRunner.InnerLaunchAsync(
            NoTool,
            page,
            pageTarget,
            null,
            document,
            Resolves(Other(isMdi: false)));

        await Assert.That(result).IsEqualTo(LaunchResult.NoDiffToolFound);
        await Assert.That(owner.Heard).IsEquivalentTo([$"{ViewerVerb.Move}:{page}:{pageTarget} from {document}"]);
    }

    /// <summary>
    /// And so does a viewer with no documents folder, which shows the document as nothing it can
    /// read: the copy bundled in the package, for one. Where the page's own tool is that viewer,
    /// the page is shown, as a pair of its own, and says what it was derived from.
    /// </summary>
    [Test]
    public async Task AViewerThatCannotDrawTheDocumentShowsItsDerivedFilesAsPairs()
    {
        using var owner = new Recording();
        using var enabled = new Enabled();
        var viewer = Viewer(documents: false);

        var result = await DiffRunner.InnerLaunchAsync(
            Resolves(viewer),
            page,
            pageTarget,
            null,
            document,
            Resolves(viewer));

        await Assert.That(result).IsEqualTo(LaunchResult.AlreadyRunningAndSupportsRefresh);
        await Assert.That(owner.Heard).IsEquivalentTo([$"{ViewerVerb.Diff}:{page}:{pageTarget} from {document}"]);
    }

    /// <summary>
    /// Launching turned off turns off the launch, not the tracking, as for every pair. Nothing
    /// was opened for the document either, so its page is tracked as any pair is, source said.
    /// </summary>
    [Test]
    public async Task WhileDisabledADerivedFileIsStillTracked()
    {
        using var owner = new Recording();
        var previousDisabled = DiffRunner.Disabled;
        DiffRunner.Disabled = true;
        try
        {
            var result = await DiffRunner.InnerLaunchAsync(
                NeverResolved,
                page,
                pageTarget,
                null,
                document,
                Resolves(Viewer(documents: true)));

            await Assert.That(result).IsEqualTo(LaunchResult.Disabled);
        }
        finally
        {
            DiffRunner.Disabled = previousDisabled;
        }

        await Assert.That(owner.Heard).IsEquivalentTo([$"{ViewerVerb.Move}:{page}:{pageTarget} from {document}"]);
    }

    /// <summary>
    /// What the viewer draws is decided by the copy that resolved and by the file: a copy with its
    /// documents folder, and a file that folder reads. An SVG is the one it reaches as the text
    /// tool, so the extension that resolved the viewer for it says nothing either way.
    /// </summary>
    [Test]
    [Arguments(@"c:\temp\a.received.pdf", true, true)]
    [Arguments(@"c:\temp\a.received.docx", true, true)]
    [Arguments(@"c:\temp\a.received.svg", true, true)]
    [Arguments(@"c:\temp\a.received.pdf", false, false)]
    [Arguments(@"c:\temp\a.received.svg", false, false)]
    [Arguments(@"c:\temp\a.received.html", true, false)]
    [Arguments(@"c:\temp\a.received.png", true, false)]
    public async Task AViewerDrawsWhatItsDocumentsFolderReads(string file, bool documents, bool expected) =>
        await Assert.That(PendingFiles.Draws(Viewer(documents), file)).IsEqualTo(expected);

    [Test]
    public async Task AnotherToolDrawsNothing() =>
        await Assert.That(PendingFiles.Draws(Other(isMdi: false), document)).IsFalse();

    /// <summary>
    /// A page a document no longer has: the delete of its verified file, saying which document.
    /// </summary>
    [Test]
    public async Task ADerivedDeleteReachesTheOwnerWithItsSource()
    {
        using var owner = new Recording();
        using var enabled = new Enabled();

        await DiffRunner.AddDerivedDeleteAsync(stale, document);
        await DiffRunner.AddDerivedDeleteAsync(stale, document);

        await Assert.That(owner.Heard).IsEquivalentTo(
        [
            $"{ViewerVerb.Delete}:{stale}: from {document}",
            $"{ViewerVerb.Delete}:{stale}: from {document}"
        ]);
    }

    /// <summary>
    /// And an ordinary delete says nothing of one, as it never has.
    /// </summary>
    [Test]
    public async Task ADeleteWithNoSourceSaysNothingOfOne()
    {
        using var owner = new Recording();
        using var enabled = new Enabled();

        await DiffRunner.AddDeleteAsync(stale);

        await Assert.That(owner.Heard).IsEquivalentTo([$"{ViewerVerb.Delete}:{stale}:"]);
    }

    /// <summary>
    /// A file is not derived from itself, and one that says it is would be hidden beneath an
    /// entry that is not there. Said by the library, so no owner has to guard against it.
    /// </summary>
    [Test]
    public async Task AFileNamingItselfAsItsSourceHasNone()
    {
        using var owner = new Recording();
        using var enabled = new Enabled();

        await DiffRunner.AddDerivedDeleteAsync(stale, stale);

        await Assert.That(owner.Heard).IsEquivalentTo([$"{ViewerVerb.Delete}:{stale}:"]);
    }

    const string document = @"c:\temp\Sample.Test.received.pdf";
    const string page = @"c:\temp\Sample.Test#page_0001.received.png";
    const string pageTarget = @"c:\code\Sample.Test#page_0001.verified.png";

    static DiffRunner.TryResolveTool Resolves(ResolvedTool tool) =>
        ([NotNullWhen(true)] out ResolvedTool? resolved) =>
        {
            resolved = tool;
            return true;
        };

    static bool NoTool([NotNullWhen(true)] out ResolvedTool? resolved)
    {
        resolved = null;
        return false;
    }

    static bool NeverResolved([NotNullWhen(true)] out ResolvedTool? resolved) =>
        throw new("A file shown beneath its source has no tool to resolve.");

    /// <summary>
    /// Launching switched on for a test, and put back after it. DisabledChecker turns it off for
    /// build servers and AI CLIs, and these drive the real launch path.
    /// </summary>
    sealed class Enabled :
        IDisposable
    {
        readonly bool previous = DiffRunner.Disabled;

        public Enabled() =>
            DiffRunner.Disabled = false;

        public void Dispose() =>
            DiffRunner.Disabled = previous;
    }

    static ResolvedTool Other(bool isMdi) =>
        new(
            name: "Fake",
            exePath: Environment.ProcessPath!,
            launchArguments: new(
                Left: (temp, target) => $"\"{target}\" \"{temp}\"",
                Right: (temp, target) => $"\"{temp}\" \"{target}\""),
            isMdi: isMdi,
            autoRefresh: false,
            binaryExtensions: [],
            requiresTarget: false,
            supportsText: true,
            useShellExecute: false);

    const string temp = @"c:\temp\Sample.Test.received.png";
    const string target = @"c:\code\Sample.Test.verified.png";
    const string stale = @"c:\code\Sample.Stale.verified.txt";

    /// <summary>
    /// Carries the identity the route branches on. Never started: an owner answers every time.
    /// </summary>
    /// <param name="documents">
    /// Whether it is a copy with its documents folder, which is said by the extensions it was
    /// resolved with: a copy that has one is given the routed ones.
    /// </param>
    static ResolvedTool Viewer(bool documents = false) =>
        new(
            name: nameof(DiffTool.DiffEngineViewer),
            tool: DiffTool.DiffEngineViewer,
            exePath: Environment.ProcessPath!,
            launchArguments: new(
                Left: (temp, target) => $"\"{target}\" \"{temp}\"",
                Right: (temp, target) => $"\"{temp}\" \"{target}\""),
            isMdi: false,
            autoRefresh: false,
            binaryExtensions: documents ? DocumentExtensions.Routed : [],
            requiresTarget: false,
            supportsText: true,
            useShellExecute: false);

    /// <summary>
    /// A queue owner that only writes down what it was asked, so the assertions are about the
    /// wire rather than about anything a real owner would go on to do.
    /// </summary>
    sealed class Recording :
        IDisposable
    {
        readonly ViewerServer server;
        readonly CancelSource cancel = new();
        readonly Task listening;
        readonly string? previousPort;
        readonly bool previousRunning;

        public List<string> Heard { get; } = [];

        /// <summary>
        /// The verb this owner is too old to understand.
        /// </summary>
        public ViewerVerb? Refuse { get; init; }

        public Recording()
        {
            if (!ViewerServer.TryBind(0, out var bound))
            {
                throw new("Could not bind an ephemeral port.");
            }

            server = bound;
            previousPort = Environment.GetEnvironmentVariable(ViewerClient.PortVariable);
            previousRunning = DiffEngineTray.IsRunning;
            Environment.SetEnvironmentVariable(ViewerClient.PortVariable, server.Port.ToString());
            // No tray, so the queue owner is where a pending file goes.
            DiffEngineTray.IsRunning = false;
            listening = server.Listen(
                message =>
                {
                    lock (Heard)
                    {
                        // Nothing where there is none, so what a file with no source is heard as
                        // stays what the tests from before there were sources assert
                        var from = message.Source is null ? "" : $" from {message.Source}";
                        Heard.Add($"{message.Verb}:{message.Key}:{message.Body}{from}");
                    }

                    if (message.Verb == Refuse)
                    {
                        return ViewerResponse.Error($"Unsupported verb: {message.Verb}");
                    }

                    return ViewerResponse.Success();
                },
                cancel.Token);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(ViewerClient.PortVariable, previousPort);
            DiffEngineTray.IsRunning = previousRunning;
            cancel.Cancel();
            server.Dispose();
            try
            {
                listening.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // Cancellation unwinds through the listener; nothing to report.
            }

            cancel.Dispose();
        }
    }

    /// <summary>
    /// A port that was free and was let go, so nothing can answer on it.
    /// </summary>
    sealed class NoOwner :
        IDisposable
    {
        readonly string? previousPort;
        readonly bool previousRunning;

        public NoOwner()
        {
            if (!ViewerServer.TryBind(0, out var bound))
            {
                throw new("Could not bind an ephemeral port.");
            }

            var port = bound.Port;
            bound.Dispose();
            previousPort = Environment.GetEnvironmentVariable(ViewerClient.PortVariable);
            previousRunning = DiffEngineTray.IsRunning;
            Environment.SetEnvironmentVariable(ViewerClient.PortVariable, port.ToString());
            DiffEngineTray.IsRunning = false;
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(ViewerClient.PortVariable, previousPort);
            DiffEngineTray.IsRunning = previousRunning;
        }
    }
}
