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
/// The tray check is <see cref="TrayAvailable"/>, whose first half is
/// <see cref="DiffEngineTray.IsRunning"/>, read once when that type initialises. A tray started
/// after the test process therefore never sees the piper port for the rest of that process's life,
/// and its moves and deletes arrive here instead — which a tray that owns the queue answers, so
/// they end up tracked either way.
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
    /// <summary>
    /// Whether the tray is the surface for a pending file: one is running, and this process has
    /// not opted out with <see cref="DiffRunner.TrayDisabled"/>.
    /// <para>
    /// One property rather than the check at each of the six sends, so opting out cannot cover
    /// some of them. Read per send, because the opt out is a setting a test moves and puts back
    /// while <see cref="DiffEngineTray.IsRunning"/> is fixed for the life of the process.
    /// </para>
    /// </summary>
    static bool TrayAvailable =>
        DiffEngineTray.IsRunning &&
        !DiffRunner.TrayDisabled;

    public static void AddDelete(string file)
    {
        if (TrayAvailable &&
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
        if (TrayAvailable &&
            await PiperClient.SendDeleteAsync(file, cancel))
        {
            return;
        }

        if (await ViewerClient.TrySendAsync(new(ViewerVerb.Delete, file), cancel))
        {
            return;
        }

        await ViewerLaunchGate.LaunchAsync(
            () => ViewerClient.TrySendAsync(new(ViewerVerb.Delete, file), cancel),
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
    public static LaunchResult AddDiff(ResolvedTool tool, string tempFile, string targetFile)
    {
        // No process, and the arguments and CanKill from the one place that answers that, because
        // the tray works out the same two values for itself when a move arrives without them.
        var (arguments, canKill) = RelaunchFor(tool, tempFile, targetFile);
        if (TrayAvailable &&
            PiperClient.SendMove(tempFile, targetFile, tool.ExePath, arguments, canKill, null))
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
    /// <para>
    /// A capped one reports what every other tool's does, rather than being folded in with a tool
    /// that could not be found: the pair has a tool and the cap is why no window opened.
    /// </para>
    /// </summary>
    static LaunchResult Launched(ViewerLaunchOutcome outcome) =>
        outcome switch
        {
            ViewerLaunchOutcome.Launched => LaunchResult.StartedNewInstance,
            ViewerLaunchOutcome.Taken => LaunchResult.AlreadyRunningAndSupportsRefresh,
            ViewerLaunchOutcome.Capped => LaunchResult.TooManyRunningDiffTools,
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
    public static async Task<LaunchResult> AddDiffAsync(ResolvedTool tool, string tempFile, string targetFile, Cancel cancel)
    {
        var (arguments, canKill) = RelaunchFor(tool, tempFile, targetFile);
        if (TrayAvailable &&
            await PiperClient.SendMoveAsync(tempFile, targetFile, tool.ExePath, arguments, canKill, null, cancel))
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
                () => ViewerClient.TrySendAsync(new(ViewerVerb.Diff, tempFile, targetFile), cancel),
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

    /// <summary>
    /// The same question asked of a move that is already tracked, where all that survives of the
    /// tool is the executable it was recorded with.
    /// <para>
    /// By file name rather than through <see cref="DiffTools.TryFindByPath"/>, which is an exact
    /// path lookup: the sender resolved the viewer bundled inside its own DiffEngine package and
    /// a tray carries a copy of its own, so the two paths are never the same string.
    /// </para>
    /// </summary>
    public static bool IsViewerExe(string? exe) =>
        exe != null &&
        viewerExeNames.Contains(Path.GetFileName(exe));

    // Read off the definition rather than spelled again here, so renaming the executable cannot
    // leave this matching the old name. Every OS's name, because the string being tested arrived
    // from another process rather than from this one.
    static readonly HashSet<string> viewerExeNames = ViewerExeNames();

    static HashSet<string> ViewerExeNames()
    {
        var support = Definitions.Tools
            .Single(_ => _.Tool == DiffTool.DiffEngineViewer)
            .OsSupport;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var settings in new[]
                 {
                     support.Windows,
                     support.Linux,
                     support.Osx
                 })
        {
            if (settings != null)
            {
                names.Add(settings.ExeName);
            }
        }

        return names;
    }

    /// <summary>
    /// How a tracked move is opened again, and whether the window that opens may be killed.
    /// <para>
    /// Answered here rather than at each caller, because there are two: this file, sending the
    /// move to a tray, and the tray itself, working them out from the extension for a move that
    /// arrived without them. The two disagreeing is not theoretical - the viewer's declared
    /// arguments are still the plain two path form, so the tray's answer reopened a pair in a
    /// window of its own while the queue it belongs to was on screen behind it.
    /// </para>
    /// <para>
    /// A viewer is never killable. It draws every pending pair in one window, so killing the one
    /// a pair was opened from takes the rest with it.
    /// </para>
    /// </summary>
    public static (string arguments, bool canKill) RelaunchFor(ResolvedTool tool, string temp, string target)
    {
        if (IsViewer(tool))
        {
            return (ViewerLauncher.DiffArguments(temp, target), false);
        }

        return (tool.GetArguments(temp, target), !tool.IsMdi);
    }

    public static void AddMove(
        string tempFile,
        string targetFile,
        string? exe,
        string? arguments,
        bool canKill,
        int? processId)
    {
        if (TrayAvailable &&
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
        if (TrayAvailable &&
            await PiperClient.SendMoveAsync(tempFile, targetFile, exe, arguments, canKill, processId, cancel))
        {
            return;
        }

        await ViewerClient.TrySendAsync(new(ViewerVerb.Move, tempFile, targetFile), cancel);
    }
}
