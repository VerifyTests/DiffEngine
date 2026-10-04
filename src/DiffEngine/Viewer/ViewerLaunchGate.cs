namespace DiffEngine;

/// <summary>
/// What a gated launch settled on.
/// </summary>
enum ViewerLaunchOutcome
{
    /// <summary>
    /// An owner appeared while this call was queued behind the gate and has taken the work, so
    /// nothing was started.
    /// </summary>
    Taken,

    /// <summary>
    /// This call started the viewer.
    /// </summary>
    Launched,

    /// <summary>
    /// Nothing could be started, or what was started exited with a failure before anything held
    /// the queue, and nobody was there to take it.
    /// </summary>
    Failed,

    /// <summary>
    /// Nobody was there to take it and <see cref="MaxInstance" /> had no slot left, so nothing was
    /// started.
    /// </summary>
    Capped
}

/// <summary>
/// One viewer launch at a time, per process, with the send retried inside the gate.
/// <para>
/// A parallel run reaches the launch path once per failing snapshot, and while nothing owns the
/// port every one of them is entitled to start a viewer. Twenty failing pairs meant twenty
/// processes: one bound the port, and the other nineteen handed their work over and exited. The
/// outcome is correct - that racing resolution is what makes it correct - but it is twenty process
/// starts to open one window, and it reported twenty new instances when there was one.
/// <c>MaxInstance</c> caps this for every tool that opens a window per pair, and does not apply to
/// the one that does not.
/// </para>
/// <para>
/// So the first caller through starts a viewer and holds the gate until that viewer answers, and
/// everyone behind it finds an owner and never launches at all. Held across the wait rather than
/// released at the start, because a viewer takes most of a second to bind and a gate let go before
/// then only lets the next caller start a second one.
/// </para>
/// <para>
/// What the gate holds is the decision, not the work. Inside it is a connect that asks whether
/// anyone is there; the send that hands the payload over happens outside, so the callers that find
/// an owner still reach it at once. Sending inside instead turned twenty process starts into
/// nineteen serialised round trips, which was slower than the problem.
/// </para>
/// <para>
/// Per process rather than per machine. Two test assemblies running at once still race, which is
/// the case the bind resolution was written for and still handles - and a named mutex would put a
/// cross process wait on the failing path of every run to save a handful of starts in the rarer
/// arrangement.
/// </para>
/// <para>
/// The gate is also where <see cref="MaxInstance" /> is charged for a viewer, because it is the one
/// place that knows whether a window is about to be opened. Handing a pair to a viewer that is
/// already up is not a new instance and spends nothing, which is why the caller cannot ask: it
/// would charge all twenty of the callers above for the one window between them. Asked after the
/// ownership probe, so the nineteen that find an owner still forward their work when the cap is
/// long since reached. And given back when the launch fails, since no window came of it.
/// </para>
/// </summary>
static class ViewerLaunchGate
{
    static SemaphoreSlim gate = new(1, 1);

    /// <summary>
    /// How long the caller that launched holds the gate waiting for its viewer to answer. Long
    /// enough for a cold start with an antivirus in the way; one that is running and never binds
    /// costs a single caller this wait, and then the next tries again. One that has exited with a
    /// failure costs only as long as that takes to notice.
    /// </summary>
    internal static TimeSpan BindWait { get; set; } = TimeSpan.FromSeconds(5);

    /// <param name="retry">
    /// The send, run again once an owner exists. Outside the gate, because it carries a payload
    /// and takes a round trip: nineteen of those queued behind one another cost more than the
    /// nineteen processes this exists to avoid.
    /// </param>
    /// <param name="launch">
    /// Starts a viewer and hands back its process, which is how the wait tells one that has given
    /// up from one that is only slow. Null when nothing could be started. Disposed here once the
    /// wait is over.
    /// </param>
    /// <param name="isOwned">
    /// How the gate asks whether anyone holds the queue, which is also what it waits on after a
    /// launch. Defaults to the real port. Supplied by the tests, which otherwise have to arrange
    /// twenty concurrent connects to a port nothing is listening on and read the answer back out
    /// of the operating system.
    /// </param>
    /// <param name="canLaunch">
    /// Whether a slot is available, and spends one when it is. Defaults to <see cref="MaxInstance" />.
    /// Supplied by the tests that are about the gate rather than about the cap, since the count it
    /// reads is shared with every other launch the process has made.
    /// </param>
    /// <param name="giveBack">
    /// Returns the slot <paramref name="canLaunch" /> spent, for a launch that then failed: the
    /// cap is on windows, and that one opened none. Kept, five viewers that could not be run - a
    /// copy too old for its arguments, one with no runtime - used up the cap, and every pair
    /// after them was told too many diff tools were running when none was. Defaults to
    /// <see cref="MaxInstance" /> where the slot was asked of it, and to nothing where a test
    /// supplied the asking.
    /// </param>
    public static ViewerLaunchOutcome Launch(
        Func<bool> retry,
        Func<Process?> launch,
        Func<bool>? isOwned = null,
        Func<bool>? canLaunch = null,
        Action? giveBack = null)
    {
        isOwned ??= () => ViewerClient.IsOwned();
        giveBack ??= canLaunch is null ? MaxInstance.GiveBack : () => { };
        canLaunch ??= () => !MaxInstance.Reached();
        bool owned;
        gate.Wait();
        try
        {
            // Asked rather than sent, so the decision to launch costs a connect rather than a
            // round trip with a payload on it.
            owned = isOwned();
            if (!owned)
            {
                if (!canLaunch())
                {
                    return ViewerLaunchOutcome.Capped;
                }

                using var viewer = launch();
                if (viewer is null ||
                    !WaitForBind(viewer, isOwned))
                {
                    giveBack();
                    return ViewerLaunchOutcome.Failed;
                }
            }
        }
        finally
        {
            gate.Release();
        }

        if (!owned)
        {
            return ViewerLaunchOutcome.Launched;
        }

        return retry() ? ViewerLaunchOutcome.Taken : ViewerLaunchOutcome.Failed;
    }

    /// <inheritdoc cref="Launch" />
    /// <remarks>
    /// Nothing done while the gate is held resumes on the caller's context: the launch runs on the
    /// pool, and the waits do not capture. The sync <see cref="Launch" /> blocks its thread on the
    /// gate, and on a single threaded context - xUnit v2's with one worker, or a UI thread - that
    /// thread is the only one a captured continuation could run on, so a sync caller behind an
    /// async one waited for a gate that could only be released by the thread doing the waiting.
    /// The launch goes to the pool rather than just being awaited without capture, because it
    /// awaits things of its own (ViewerLauncher's payload write) and those capture whatever context
    /// is current when it starts.
    /// </remarks>
    public static async Task<ViewerLaunchOutcome> LaunchAsync(
        Func<Task<bool>> retry,
        Func<Task<Process?>> launch,
        Cancel cancel,
        Func<bool>? isOwned = null,
        Func<bool>? canLaunch = null,
        Action? giveBack = null)
    {
        isOwned ??= () => ViewerClient.IsOwned();
        giveBack ??= canLaunch is null ? MaxInstance.GiveBack : () => { };
        canLaunch ??= () => !MaxInstance.Reached();
        bool owned;
        await gate.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            owned = isOwned();
            if (!owned)
            {
                if (!canLaunch())
                {
                    return ViewerLaunchOutcome.Capped;
                }

                using var viewer = await Task.Run(launch, cancel).ConfigureAwait(false);
                if (viewer is null ||
                    !await WaitForBindAsync(viewer, isOwned, cancel).ConfigureAwait(false))
                {
                    giveBack();
                    return ViewerLaunchOutcome.Failed;
                }
            }
        }
        finally
        {
            gate.Release();
        }

        if (!owned)
        {
            return ViewerLaunchOutcome.Launched;
        }

        return await retry() ? ViewerLaunchOutcome.Taken : ViewerLaunchOutcome.Failed;
    }

    /// <summary>
    /// Waits for the launched viewer to be answerable, so the next caller through the gate finds
    /// an owner rather than starting another. Gives up after <see cref="BindWait" /> and reports
    /// the launch all the same, because it did happen: the work went over on the command line or
    /// in a payload file, and the cost of giving up early is one more viewer, which is where this began.
    /// <para>
    /// False when the viewer gave up first, which is the one launch that did not happen. It used to
    /// be waited on for the whole of <see cref="BindWait" /> with the gate held and then reported
    /// like any other, so an inline snapshot was said to be queued when it was nowhere: not in a
    /// queue, and not staged either, since a caller stages only what it is told nobody took.
    /// </para>
    /// </summary>
    static bool WaitForBind(Process viewer, Func<bool> isOwned)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < BindWait)
        {
            // Before the probe, so that an owner some other process started is not taken for the
            // viewer this one did: that owner was never handed the work
            if (GaveUp(viewer))
            {
                return false;
            }

            if (isOwned())
            {
                return true;
            }

            Thread.Sleep(poll);
        }

        return true;
    }

    /// <inheritdoc cref="WaitForBind" />
    static async Task<bool> WaitForBindAsync(Process viewer, Func<bool> isOwned, Cancel cancel)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < BindWait)
        {
            if (GaveUp(viewer))
            {
                return false;
            }

            if (isOwned())
            {
                return true;
            }

            await Task.Delay(poll, cancel).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// Whether the viewer this call started has exited and said it failed, so it took nothing and
    /// never will. A copy from before the arguments it was given exits on the first it does not
    /// know, and an apphost with no runtime to run exits before any of the viewer's own code.
    /// <para>
    /// A clean exit is not this, and is left to the wait. A viewer that finds the port already
    /// bound hands its work to whoever holds it and exits with zero, and the next probe finds that
    /// owner. One that opened, was dealt with and closed between two probes exits with zero too,
    /// having staged whatever it still held.
    /// </para>
    /// <para>
    /// A process that cannot be asked is taken to be running, which is the answer that leaves the
    /// wait as it was before there was a process to ask.
    /// </para>
    /// </summary>
    static bool GaveUp(Process viewer)
    {
        try
        {
            return viewer.HasExited &&
                   viewer.ExitCode != 0;
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    static TimeSpan poll = TimeSpan.FromMilliseconds(50);
}
