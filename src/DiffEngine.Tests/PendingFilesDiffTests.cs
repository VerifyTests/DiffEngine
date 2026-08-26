// DiffEngineTray is the obsolete public shim, but its IsRunning is still where the tray check
// lives, and these tests have to hold it down.
#pragma warning disable CS0618

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

        var result = await PendingFiles.AddDiffAsync(Viewer(), Temp, Target, Cancel.None);

        await Assert.That(result).IsEqualTo(LaunchResult.AlreadyRunningAndSupportsRefresh);
        await Assert.That(owner.Heard).IsEquivalentTo([$"{ViewerVerb.Diff}:{Temp}:{Target}"]);
    }

    [Test]
    public async Task ASyncDiffReachesTheOwnerToo()
    {
        using var owner = new Recording();

        var result = PendingFiles.AddDiff(Viewer(), Temp, Target);

        await Assert.That(result).IsEqualTo(LaunchResult.AlreadyRunningAndSupportsRefresh);
        await Assert.That(owner.Heard).IsEquivalentTo([$"{ViewerVerb.Diff}:{Temp}:{Target}"]);
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

        var result = await PendingFiles.AddDiffAsync(Viewer(), Temp, Target, Cancel.None);

        await Assert.That(result).IsEqualTo(LaunchResult.AlreadyRunningAndSupportsRefresh);
        await Assert.That(owner.Heard).IsEquivalentTo(
        [
            $"{ViewerVerb.Diff}:{Temp}:{Target}",
            $"{ViewerVerb.Move}:{Temp}:{Target}"
        ]);
    }

    [Test]
    public async Task ASyncRefusedDiffFallsBackToAPlainMove()
    {
        using var owner = new Recording {Refuse = ViewerVerb.Diff};

        PendingFiles.AddDiff(Viewer(), Temp, Target);

        await Assert.That(owner.Heard).IsEquivalentTo(
        [
            $"{ViewerVerb.Diff}:{Temp}:{Target}",
            $"{ViewerVerb.Move}:{Temp}:{Target}"
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
            var result = await PendingFiles.AddDiffAsync(Viewer(), Temp, Target, Cancel.None);

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

        PendingFiles.SettleDiff(Temp);

        await Assert.That(owner.Heard).IsEquivalentTo([$"{ViewerVerb.Settle}:{TrackedKeys.ForMove(Temp)}:"]);
    }

    /// <summary>
    /// Nobody owning the queue means no row to drop, which is the goal state already. Silent
    /// rather than reported, the same bargain a pending delete with no surface makes.
    /// </summary>
    [Test]
    public async Task SettlingWithNoOwnerIsSilent()
    {
        using var absent = new NoOwner();

        await Assert.That(() => PendingFiles.SettleDiff(Temp)).ThrowsNothing();
    }

    /// <summary>
    /// The tray works the arguments out for itself when a move arrives without them, and used to
    /// take the viewer's declared ones - two plain paths, which open a window of its own for a
    /// pair whose queue is already on screen. Both callers ask this instead.
    /// </summary>
    [Test]
    public async Task AViewerIsReopenedIntoItsQueueAndNeverKilled()
    {
        var (arguments, canKill) = PendingFiles.RelaunchFor(Viewer(), Temp, Target);

        await Assert.That(arguments).IsEqualTo($"--diff \"{Temp}\" \"{Target}\"");
        // One window holds every pending pair, so killing it takes the rest with it.
        await Assert.That(canKill).IsFalse();
    }

    [Test]
    public async Task AnOrdinaryToolKeepsItsOwnArgumentsAndStaysKillable()
    {
        var (arguments, canKill) = PendingFiles.RelaunchFor(Other(isMdi: false), Temp, Target);

        await Assert.That(arguments).IsEqualTo($"\"{Temp}\" \"{Target}\"");
        await Assert.That(canKill).IsTrue();
    }

    [Test]
    public async Task AnMdiToolIsNotKillableEither()
    {
        var (_, canKill) = PendingFiles.RelaunchFor(Other(isMdi: true), Temp, Target);

        await Assert.That(canKill).IsFalse();
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

    const string Temp = @"c:\temp\Sample.Test.received.png";
    const string Target = @"c:\code\Sample.Test.verified.png";

    /// <summary>
    /// Carries the identity the route branches on. Never started: an owner answers every time.
    /// </summary>
    static ResolvedTool Viewer() =>
        new(
            name: DiffTool.DiffEngineViewer.ToString(),
            tool: DiffTool.DiffEngineViewer,
            exePath: Environment.ProcessPath!,
            launchArguments: new(
                Left: (temp, target) => $"\"{target}\" \"{temp}\"",
                Right: (temp, target) => $"\"{temp}\" \"{target}\""),
            isMdi: false,
            autoRefresh: false,
            binaryExtensions: [],
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
                        Heard.Add($"{message.Verb}:{message.Key}:{message.Body}");
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
