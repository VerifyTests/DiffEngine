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

    /// <summary>
    /// <paramref name="source" /> is the received file of the pending move this delete was derived
    /// from, or null: a page a document no longer has, whose document is pending. It rides both
    /// sends. It does not ride the launch, which has only a command line an older copy has to be
    /// able to read: a delete that had to start its own viewer is held as an ordinary one.
    /// </summary>
    public static void AddDelete(string file, string? source = null)
    {
        if (TrayAvailable &&
            PiperClient.SendDelete(file, source))
        {
            return;
        }

        if (ViewerClient.TrySend(Delete(file, source)))
        {
            return;
        }

        ViewerLaunchGate.Launch(
            () => ViewerClient.TrySend(Delete(file, source)),
            () => ViewerLauncher.LaunchDelete(file));
    }

    /// <inheritdoc cref="AddDelete"/>
    public static async Task AddDeleteAsync(string file, Cancel cancel, string? source = null)
    {
        if (TrayAvailable &&
            await PiperClient.SendDeleteAsync(file, cancel, source))
        {
            return;
        }

        if (await ViewerClient.TrySendAsync(Delete(file, source), cancel))
        {
            return;
        }

        await ViewerLaunchGate.LaunchAsync(
            () => ViewerClient.TrySendAsync(Delete(file, source), cancel),
            () => Task.FromResult(ViewerLauncher.LaunchDelete(file)),
            cancel);
    }

    static ViewerMessage Delete(string file, string? source) =>
        new(ViewerVerb.Delete, file)
        {
            Source = source
        };

    static ViewerMessage Pair(ViewerVerb verb, string tempFile, string targetFile, string? source) =>
        new(verb, tempFile, targetFile)
        {
            Source = source
        };

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
    /// tray tracks it and the queue owner - normally that same tray - only has to raise something.
    /// Marked <see cref="ViewerMessage.Arrived" />, so the pair joins the queue behind whatever is
    /// being read rather than taking the selection, as a <see cref="ViewerVerb.Diff" /> does.
    /// </para>
    /// <para>
    /// A refused focus falls through to <see cref="ViewerVerb.Diff" /> rather than being discarded,
    /// because the two sends are two connections and nothing orders them. The piper send is fire
    /// and forget: it reports that the bytes went out, not that the move was tracked, and the tray
    /// reads that connection on a task of its own - through a solution directory walk, on the first
    /// move for a path - while the focus is already asking about a key that has not landed. Focus
    /// refuses an unknown key and raises nothing, so losing that race was a first run where the
    /// pair reached the tray menu and no window ever opened, and a second run where the same key
    /// was still tracked and one did. Diff tracks and raises in a single message to a single
    /// process, so there is no order left to get wrong, and its tracking is keyed on the received
    /// file like the piper move's, so whichever lands second updates the one entry.
    /// </para>
    /// <para>
    /// It is also the answer in the arrangement where a viewer owns the queue while a tray runs:
    /// that viewer does not know the tray's files, so the focus can never find the key. The pair is
    /// tracked on both sides there rather than shown by neither.
    /// </para>
    /// <para>
    /// <paramref name="source" /> is what the pair was derived from, when the viewer is the tool
    /// for a file whose source it is not drawing: it rides every send, and not the launch, as on
    /// <see cref="AddDelete" />.
    /// </para>
    /// </summary>
    public static LaunchResult AddDiff(ResolvedTool tool, string tempFile, string targetFile, string? source = null)
    {
        // No process, and the arguments and CanKill from the one place that answers that, because
        // the tray works out the same two values for itself when a move arrives without them.
        var (arguments, canKill) = RelaunchFor(tool, tempFile, targetFile);
        if (TrayAvailable &&
            PiperClient.SendMove(tempFile, targetFile, tool.ExePath, arguments, canKill, null, source) &&
            ViewerClient.TrySend(new(ViewerVerb.Focus, TrackedKeys.ForMove(tempFile), ViewerMessage.Arrived)))
        {
            return LaunchResult.AlreadyRunningAndSupportsRefresh;
        }

        // A port recently found unowned is not asked again: the gate below probes for itself
        // before launching, and its probe corrects the memory when an owner has arrived since
        if (ViewerClient.TrySend(Pair(ViewerVerb.Diff, tempFile, targetFile, source), out var response, skipIfUnowned: true))
        {
            return response.Ok
                ? LaunchResult.AlreadyRunningAndSupportsRefresh
                : Refused(tempFile, targetFile, source);
        }

        return Launched(
            ViewerLaunchGate.Launch(
                () => ViewerClient.TrySend(Pair(ViewerVerb.Diff, tempFile, targetFile, source)),
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
    /// <para>
    /// A viewer that was started and exited with a failure is folded in with it. The copy that
    /// resolved cannot be run, which from here is a tool that is not there, and it used to be
    /// reported as a new instance with no window behind it.
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
    static LaunchResult Refused(string tempFile, string targetFile, string? source) =>
        ViewerClient.TrySend(Pair(ViewerVerb.Move, tempFile, targetFile, source))
            ? LaunchResult.AlreadyRunningAndSupportsRefresh
            : LaunchResult.NoDiffToolFound;

    /// <inheritdoc cref="AddDiff"/>
    public static async Task<LaunchResult> AddDiffAsync(ResolvedTool tool, string tempFile, string targetFile, Cancel cancel, string? source = null)
    {
        var (arguments, canKill) = RelaunchFor(tool, tempFile, targetFile);
        if (TrayAvailable &&
            await PiperClient.SendMoveAsync(tempFile, targetFile, tool.ExePath, arguments, canKill, null, cancel, source) &&
            await ViewerClient.TrySendAsync(new(ViewerVerb.Focus, TrackedKeys.ForMove(tempFile), ViewerMessage.Arrived), cancel))
        {
            return LaunchResult.AlreadyRunningAndSupportsRefresh;
        }

        var outcome = await ViewerClient.SendAsync(Pair(ViewerVerb.Diff, tempFile, targetFile, source), cancel, skipIfUnowned: true);
        if (outcome == SendOutcome.Accepted)
        {
            return LaunchResult.AlreadyRunningAndSupportsRefresh;
        }

        if (outcome == SendOutcome.Refused)
        {
            return await ViewerClient.TrySendAsync(Pair(ViewerVerb.Move, tempFile, targetFile, source), cancel)
                ? LaunchResult.AlreadyRunningAndSupportsRefresh
                : LaunchResult.NoDiffToolFound;
        }

        return Launched(
            await ViewerLaunchGate.LaunchAsync(
                () => ViewerClient.TrySendAsync(Pair(ViewerVerb.Diff, tempFile, targetFile, source), cancel),
                () => Task.FromResult(ViewerLauncher.LaunchDiff(tempFile, targetFile)),
                cancel));
    }

    /// <summary>
    /// Whether <paramref name="tool" /> will show <paramref name="file" /> as a document: its text,
    /// and its pages drawn. That is the viewer, a copy of it that has its documents folder, and a
    /// file of a type that folder reads.
    /// <para>
    /// It is what decides whether a file derived from <paramref name="file" /> needs a window of
    /// its own (see <see cref="AddDerived" />). A page of a document the viewer is drawing is
    /// already on screen, beside the page it replaces. The same page beside a document Word or
    /// Beyond Compare is showing, or beside a text file the viewer is showing as text, is not, and
    /// is opened as it always has been.
    /// </para>
    /// </summary>
    public static bool Draws(ResolvedTool tool, string file) =>
        IsViewer(tool) &&
        DocumentExtensions.Is(file) &&
        ViewerDocuments.ReadBy(tool);

    /// <summary>
    /// A pending file derived from a document the viewer is drawing: tracked, and nothing opened
    /// for it. True when something took it, and false when nothing did, which leaves the caller
    /// to open it as any other pair.
    /// <para>
    /// Tracked as a pair the viewer shows, on both routes. To a tray that means the viewer's own
    /// executable and the arguments <see cref="AddDiff" /> sends, so the pair counts as open - the
    /// window it is drawn in is on screen - and "accept all open" takes the pages with the
    /// document rather than leaving them behind, and "open diff tool" raises the queue the row is
    /// in. Without the focus <see cref="AddDiff" /> follows that with: the document has the
    /// window, and this row sits beneath it.
    /// </para>
    /// <para>
    /// Never through the launch gate. The source went first, and its own send held the gate until
    /// a viewer had the queue, so nobody answering here means the source is in no viewer either:
    /// capped, failed to start, or closed since. A viewer started for a page could not be told
    /// what the page was derived from, which is the only reason to start one for it.
    /// </para>
    /// </summary>
    public static bool AddDerived(ResolvedTool viewer, string tempFile, string targetFile, string source)
    {
        var (arguments, canKill) = RelaunchFor(viewer, tempFile, targetFile);
        if (TrayAvailable &&
            PiperClient.SendMove(tempFile, targetFile, viewer.ExePath, arguments, canKill, null, source))
        {
            return true;
        }

        return ViewerClient.TrySend(Pair(ViewerVerb.Move, tempFile, targetFile, source));
    }

    /// <inheritdoc cref="AddDerived"/>
    public static async Task<bool> AddDerivedAsync(ResolvedTool viewer, string tempFile, string targetFile, string source, Cancel cancel)
    {
        var (arguments, canKill) = RelaunchFor(viewer, tempFile, targetFile);
        if (TrayAvailable &&
            await PiperClient.SendMoveAsync(tempFile, targetFile, viewer.ExePath, arguments, canKill, null, cancel, source))
        {
            return true;
        }

        return await ViewerClient.TrySendAsync(Pair(ViewerVerb.Move, tempFile, targetFile, source), cancel);
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
    /// The other end of <see cref="AddDelete" />: the file a delete was raised for is in use
    /// again, so the delete goes. Nothing is deleted.
    /// <para>
    /// To the queue owner, as <see cref="SettleDiff" /> is, which reaches the delete wherever it
    /// is held: in a viewer, or in a tray that owns the queue, which keeps the deletes that arrived
    /// over the piper port in the same tracked files. A tray that does not own the queue keeps its
    /// own, and the piper format that would reach it is frozen at moves and deletes.
    /// </para>
    /// <para>
    /// Silent when nobody answers: no owner means no row, which is the state this was asking for.
    /// </para>
    /// </summary>
    public static void SettleDelete(string file) =>
        ViewerClient.TrySend(new(ViewerVerb.Settle, TrackedKeys.ForDelete(file)));

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
    static HashSet<string> viewerExeNames = ViewerExeNames();

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

    /// <summary>
    /// <paramref name="source" /> is the received file of the pending move this one was derived
    /// from, or null. Here it is only said, to whoever tracks the pair: the tool that opened a
    /// window for it has opened it already.
    /// </summary>
    public static void AddMove(
        string tempFile,
        string targetFile,
        string? exe,
        string? arguments,
        bool canKill,
        int? processId,
        string? source = null)
    {
        if (TrayAvailable &&
            PiperClient.SendMove(tempFile, targetFile, exe, arguments, canKill, processId, source))
        {
            return;
        }

        ViewerClient.TrySend(Pair(ViewerVerb.Move, tempFile, targetFile, source));
    }

    /// <inheritdoc cref="AddMove"/>
    public static async Task AddMoveAsync(
        string tempFile,
        string targetFile,
        string? exe,
        string? arguments,
        bool canKill,
        int? processId,
        Cancel cancel,
        string? source = null)
    {
        if (TrayAvailable &&
            await PiperClient.SendMoveAsync(tempFile, targetFile, exe, arguments, canKill, processId, cancel, source))
        {
            return;
        }

        await ViewerClient.TrySendAsync(Pair(ViewerVerb.Move, tempFile, targetFile, source), cancel);
    }
}
