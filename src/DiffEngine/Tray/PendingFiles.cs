// DiffEngineTray is the obsolete public shim, but its IsRunning is still where the tray check
// lives, and tests still set it.
#pragma warning disable CS0618 // Type or member is obsolete

namespace DiffEngine;

/// <summary>
/// Where a pending move or delete goes.
/// <para>
/// The tray when one is running, over the piper port it has always used. Otherwise the process
/// that owns the inline queue, which is normally a viewer — so a pending file has a surface with
/// no tray installed. Before this it had none: the send was skipped outright, and a received file
/// or a stale verified file was pending in nothing at all.
/// </para>
/// <para>
/// A delete starts a viewer when nothing owns the queue. A move does not, and the asymmetry is the
/// point: <see cref="DiffRunner"/> has already opened a diff tool for that file pair, so a move
/// has a window, and a second one competing with it is not an improvement. A delete has no second
/// file to compare against and so no tool to open.
/// </para>
/// <para>
/// The tray check is <see cref="DiffEngineTray.IsRunning"/>, read once when that type initialises.
/// A tray started after the test process therefore never sees the piper port for the rest of that
/// process's life, and its moves and deletes arrive here instead — which a tray that owns the
/// queue answers, so they end up tracked either way.
/// </para>
/// <para>
/// The mirror of that case is a tray that exits while a long lived host keeps running, and it is
/// why the piper send is asked whether it connected rather than told to get on with it. The cached
/// answer still says a tray is there, so every later move and delete went to a port nobody was
/// listening on and was swallowed into a trace line: pending in nothing, with no fallback and no
/// LaunchDelete. A refused piper send now falls through to the same branch as no tray at all.
/// </para>
/// </summary>
static class PendingFiles
{
    public static void AddDelete(string file)
    {
        if (DiffEngineTray.IsRunning &&
            PiperClient.SendDelete(file))
        {
            return;
        }

        if (ViewerClient.TrySend(new(ViewerVerb.Delete, file)))
        {
            return;
        }

        ViewerLaunchGate.Launch(
            () => ViewerClient.TrySend(new(ViewerVerb.Delete, file)),
            () => ViewerLauncher.LaunchDelete(file));
    }

    public static async Task AddDeleteAsync(string file, Cancel cancel)
    {
        if (DiffEngineTray.IsRunning &&
            await PiperClient.SendDeleteAsync(file, cancel))
        {
            return;
        }

        if (await ViewerClient.TrySendAsync(new(ViewerVerb.Delete, file), cancel))
        {
            return;
        }

        await ViewerLaunchGate.LaunchAsync(
            async () => await ViewerClient.TrySendAsync(new(ViewerVerb.Delete, file), cancel),
            () => Task.FromResult(ViewerLauncher.LaunchDelete(file)),
            cancel);
    }

    /// <summary>
    /// A failing pair whose resolved diff tool is the viewer itself.
    /// <para>
    /// Tracked exactly as any other move is - the tray when one is running, the queue owner
    /// otherwise - and then shown, which is the part <see cref="ViewerVerb.Move" /> withholds.
    /// Every other tool's move arrives with that tool's window already open for the pair; this one
    /// has no window until something raises one over the entry.
    /// </para>
    /// <para>
    /// The window is a <see cref="ViewerVerb.Focus" /> when the tray took the move, because the
    /// tray tracks it and the queue owner - normally that same tray - only has to raise something
    /// over it. In the arrangement where a viewer owns the queue while a tray runs, that viewer
    /// does not know the tray's files, so the focus finds nothing and the pair stays what it was
    /// before any of this: an entry in the tray menu.
    /// </para>
    /// </summary>
    public static LaunchResult AddDiff(string tempFile, string targetFile, string exe)
    {
        // CanKill false and no process: there is no window of its own to kill, and killing the
        // shared one would take every other pair in it away as well. The arguments are stored all
        // the same, because the tray re-runs them for "Open diff tool".
        if (DiffEngineTray.IsRunning &&
            PiperClient.SendMove(tempFile, targetFile, exe, ViewerLauncher.DiffArguments(tempFile, targetFile), false, null))
        {
            ViewerClient.TrySend(new(ViewerVerb.Focus, TrackedKeys.ForMove(tempFile)));
            return LaunchResult.AlreadyRunningAndSupportsRefresh;
        }

        if (ViewerClient.TrySend(new(ViewerVerb.Diff, tempFile, targetFile), out var response))
        {
            return response.Ok
                ? LaunchResult.AlreadyRunningAndSupportsRefresh
                : Refused(tempFile, targetFile);
        }

        return Launched(
            ViewerLaunchGate.Launch(
                () => ViewerClient.TrySend(new(ViewerVerb.Diff, tempFile, targetFile)),
                () => ViewerLauncher.LaunchDiff(tempFile, targetFile)));
    }

    /// <summary>
    /// A launch that turned out not to be one is not reported as one. Twenty pairs failing at once
    /// put twenty callers on the gate and one viewer on the screen, and calling that twenty new
    /// instances is how the count stopped meaning anything.
    /// </summary>
    static LaunchResult Launched(ViewerLaunchOutcome outcome) =>
        outcome switch
        {
            ViewerLaunchOutcome.Launched => LaunchResult.StartedNewInstance,
            ViewerLaunchOutcome.Taken => LaunchResult.AlreadyRunningAndSupportsRefresh,
            _ => LaunchResult.NoDiffToolFound
        };

    /// <summary>
    /// An owner that is there and said no, which is an owner too old to know the verb. Launching a
    /// second viewer cannot change that answer and would bind nothing, so the pair goes over as a
    /// plain move: a row with nothing raised over it, which every owner has always understood.
    /// </summary>
    static LaunchResult Refused(string tempFile, string targetFile) =>
        ViewerClient.TrySend(new(ViewerVerb.Move, tempFile, targetFile))
            ? LaunchResult.AlreadyRunningAndSupportsRefresh
            : LaunchResult.NoDiffToolFound;

    /// <inheritdoc cref="AddDiff"/>
    public static async Task<LaunchResult> AddDiffAsync(string tempFile, string targetFile, string exe, Cancel cancel)
    {
        if (DiffEngineTray.IsRunning &&
            await PiperClient.SendMoveAsync(tempFile, targetFile, exe, ViewerLauncher.DiffArguments(tempFile, targetFile), false, null, cancel))
        {
            await ViewerClient.TrySendAsync(new(ViewerVerb.Focus, TrackedKeys.ForMove(tempFile)), cancel);
            return LaunchResult.AlreadyRunningAndSupportsRefresh;
        }

        var outcome = await ViewerClient.SendAsync(new(ViewerVerb.Diff, tempFile, targetFile), cancel);
        if (outcome == SendOutcome.Accepted)
        {
            return LaunchResult.AlreadyRunningAndSupportsRefresh;
        }

        if (outcome == SendOutcome.Refused)
        {
            return await ViewerClient.TrySendAsync(new(ViewerVerb.Move, tempFile, targetFile), cancel)
                ? LaunchResult.AlreadyRunningAndSupportsRefresh
                : LaunchResult.NoDiffToolFound;
        }

        return Launched(
            await ViewerLaunchGate.LaunchAsync(
                async () => await ViewerClient.TrySendAsync(new(ViewerVerb.Diff, tempFile, targetFile), cancel),
                () => Task.FromResult(ViewerLauncher.LaunchDiff(tempFile, targetFile)),
                cancel));
    }

    /// <summary>
    /// The other end of <see cref="AddDiff" />: the pair's test started passing, so the row it
    /// took goes.
    /// <para>
    /// A settle rather than a kill, because there is no process of its own to kill and the window
    /// it is drawn in holds every other pending pair. And rather than a discard, because the
    /// received file a discard would delete is one DiffEngine has already removed.
    /// </para>
    /// <para>
    /// Silent when nobody answers, the same bargain a pending file with no surface makes: no
    /// owner means no row, which is the state this was asking for.
    /// </para>
    /// </summary>
    public static void SettleDiff(string tempFile) =>
        ViewerClient.TrySend(new(ViewerVerb.Settle, TrackedKeys.ForMove(tempFile)));

    /// <summary>
    /// Whether a pending file should take the <see cref="AddDiff" /> route rather than the plain
    /// tracking one, which is exactly whether the tool that would have opened a window for it is
    /// the viewer.
    /// </summary>
    public static bool IsViewer(ResolvedTool tool) =>
        tool.Tool == DiffTool.DiffEngineViewer;

    public static void AddMove(
        string tempFile,
        string targetFile,
        string? exe,
        string? arguments,
        bool canKill,
        int? processId)
    {
        if (DiffEngineTray.IsRunning &&
            PiperClient.SendMove(tempFile, targetFile, exe, arguments, canKill, processId))
        {
            return;
        }

        ViewerClient.TrySend(new(ViewerVerb.Move, tempFile, targetFile));
    }

    public static async Task AddMoveAsync(
        string tempFile,
        string targetFile,
        string? exe,
        string? arguments,
        bool canKill,
        int? processId,
        Cancel cancel)
    {
        if (DiffEngineTray.IsRunning &&
            await PiperClient.SendMoveAsync(tempFile, targetFile, exe, arguments, canKill, processId, cancel))
        {
            return;
        }

        await ViewerClient.TrySendAsync(new(ViewerVerb.Move, tempFile, targetFile), cancel);
    }
}
