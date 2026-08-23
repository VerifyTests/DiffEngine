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
    /// Nothing could be started, and nobody was there to take it.
    /// </summary>
    Failed
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
/// </summary>
static class ViewerLaunchGate
{
    static readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>
    /// How long the caller that launched holds the gate waiting for its viewer to answer. Long
    /// enough for a cold start with an antivirus in the way; one that never binds costs a single
    /// caller this wait, and then the next tries again.
    /// </summary>
    internal static TimeSpan BindWait { get; set; } = TimeSpan.FromSeconds(5);

    /// <param name="retry">
    /// The send, run again once an owner exists. Outside the gate, because it carries a payload
    /// and takes a round trip: nineteen of those queued behind one another cost more than the
    /// nineteen processes this exists to avoid.
    /// </param>
    /// <param name="launch">Starts a viewer. False when nothing could be started.</param>
    /// <param name="isOwned">
    /// How the gate asks whether anyone holds the queue, which is also what it waits on after a
    /// launch. Defaults to the real port. Supplied by the tests, which otherwise have to arrange
    /// twenty concurrent connects to a port nothing is listening on and read the answer back out
    /// of the operating system.
    /// </param>
    public static ViewerLaunchOutcome Launch(Func<bool> retry, Func<bool> launch, Func<bool>? isOwned = null)
    {
        isOwned ??= () => ViewerClient.IsOwned();
        bool owned;
        gate.Wait();
        try
        {
            // Asked rather than sent, so the decision to launch costs a connect rather than a
            // round trip with a payload on it.
            owned = isOwned();
            if (!owned)
            {
                if (!launch())
                {
                    return ViewerLaunchOutcome.Failed;
                }

                WaitForBind(isOwned);
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
    public static async Task<ViewerLaunchOutcome> LaunchAsync(
        Func<Task<bool>> retry,
        Func<Task<bool>> launch,
        Cancel cancel,
        Func<bool>? isOwned = null)
    {
        isOwned ??= () => ViewerClient.IsOwned();
        bool owned;
        await gate.WaitAsync(cancel);
        try
        {
            owned = isOwned();
            if (!owned)
            {
                if (!await launch())
                {
                    return ViewerLaunchOutcome.Failed;
                }

                await WaitForBindAsync(isOwned, cancel);
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
    /// on stdin, and the cost of giving up early is one more viewer, which is where this began.
    /// </summary>
    static void WaitForBind(Func<bool> isOwned)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < BindWait)
        {
            if (isOwned())
            {
                return;
            }

            Thread.Sleep(Poll);
        }
    }

    /// <inheritdoc cref="WaitForBind" />
    static async Task WaitForBindAsync(Func<bool> isOwned, Cancel cancel)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < BindWait)
        {
            if (isOwned())
            {
                return;
            }

            await Task.Delay(Poll, cancel);
        }
    }

    static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(50);
}
