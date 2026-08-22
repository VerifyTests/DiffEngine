/// <summary>
/// The tray holding the inline queue itself: the socket every process talks over, the queue, and
/// the viewer launched to display it.
/// <para>
/// The tray is the durable surface. It starts at login and outlives every viewer, so a viewer that
/// crashes or is killed no longer takes the pending snapshots with it, and closing the window
/// stops meaning "keep a 52 MB process resident purely as a data store".
/// </para>
/// <para>
/// Accepting runs here, never on a UI or render thread: the listener thread for socket driven
/// accepts, and a worker the tray spins up for menu clicks. The patch is applied outside the gate
/// as well, because InlineApplier can wait ten seconds on its cross process mutex, and holding the
/// gate that long would stall every listing the attached viewer polls for — which it reads as the
/// owner having died.
/// </para>
/// <para>
/// A clean tray exit stages what is still pending back to disk (<see cref="InlineStaging"/>), so
/// a restart no longer silently discards the queue. A kill or a crash still loses it, exactly as
/// it loses tracked moves and deletes, and the recovery is the same: re-run the tests.
/// </para>
/// </summary>
sealed class OwnedInlineHost :
    IInlineHost,
    IQueueOwner,
    IAsyncDisposable
{
    readonly ViewerServer server;
    readonly CancelSource cancel = new();
    Task? listening;
    readonly Action<string> failed;
    readonly IViewerLauncher launcher;
    readonly Func<InlinePatch, InlineApplyResult> applier;
    readonly Lock gate = new();
    InlineQueue queue = InlineQueue.Empty;
    WindowCommand? window;
    string? windowKey;

    OwnedInlineHost(
        ViewerServer server,
        Action<string> failed,
        IViewerLauncher launcher,
        Func<InlinePatch, InlineApplyResult> applier)
    {
        this.server = server;
        this.failed = failed;
        this.launcher = launcher;
        this.applier = applier;
    }

    /// <summary>
    /// Null when the bind failed, which means a viewer started first and owns the queue for as
    /// long as it runs. The caller falls back to <see cref="RemoteInlineHost"/>.
    /// <para>
    /// <paramref name="port"/>, <paramref name="launcher"/> and <paramref name="applier"/> are for
    /// tests, which need an ephemeral port so a live tray on the machine is not in the way, no
    /// real window, and an apply that can be made slow or refused without arranging a locked file.
    /// </para>
    /// </summary>
    public static OwnedInlineHost? TryOwn(
        Action<string> failed,
        IViewerLauncher? launcher = null,
        int? port = null,
        Func<InlinePatch, InlineApplyResult>? applier = null) =>
        ViewerServer.TryBind(port ?? ViewerClient.Port, out var server)
            ? new(server, failed, launcher ?? new ProcessViewerLauncher(), applier ?? InlineApplier.Apply)
            : null;

    public int Port => server.Port;

    public string Description => $"owned by this tray on port {server.Port}";

    /// <summary>
    /// Raised when the queue changes from the socket rather than from the menu, so the icon lights
    /// up as a snapshot arrives instead of up to one scan later.
    /// </summary>
    public Action? Changed { get; set; }

    /// <summary>
    /// The tray's tracked moves and deletes, wired before <see cref="Start"/> like
    /// <see cref="Changed"/>, so the first listing answered already carries them. Null in tests
    /// that only exercise the queue.
    /// </summary>
    public ITrackedFiles? TrackedFiles { get; set; }

    /// <summary>
    /// Begin answering. Separate from <see cref="TryOwn"/>, because binding is the ownership claim
    /// and has to happen before anything else can take it, while serving cannot start until
    /// <see cref="Changed"/> is wired — a patch answered before then would light the icon only on
    /// the next scan. The port is already bound and backlogging by now, so nothing is refused in
    /// between.
    /// </summary>
    public void Start() =>
        listening = server.Listen(Handle, cancel.Token);

    public IReadOnlyList<PendingSnapshot> List()
    {
        lock (gate)
        {
            // Through the shared projection, so a conflicted entry reads the same here as it does
            // to a remote tray listing over the wire.
            return ViewerListing.Items(queue.Items, withPatches: false)
                .Select(_ => new PendingSnapshot(_.Key, _.Name, _.Status))
                .ToList();
        }
    }

    /// <summary>
    /// The queue is immutable, so the list can be handed out under the gate without copying it.
    /// </summary>
    public IReadOnlyList<PendingInline> Queued()
    {
        lock (gate)
        {
            return queue.Items;
        }
    }

    public AcceptOutcome Accept(PendingSnapshot snapshot, out string? message)
    {
        var (outcome, text, _) = AcceptOne(snapshot.Key, null);
        message = text;
        return outcome;
    }

    public bool Discard(PendingSnapshot snapshot, out string? message)
    {
        lock (gate)
        {
            var before = queue.Count;
            queue = queue.Discard(snapshot.Key, out message);
            return queue.Count < before;
        }
    }

    public bool AcceptAll(out string? message)
    {
        message = AcceptEvery();
        lock (gate)
        {
            // A snapshot that arrived while the batch was applying is still pending, and saying
            // "could not accept everything" is then the truth.
            return queue.Count == 0;
        }
    }

    public bool DiscardAll(out string? message)
    {
        message = ((IQueueOwner) this).DiscardAll();
        // Owned in this process, so there is nobody to fail to reach
        return true;
    }

    public void Focus(PendingSnapshot snapshot) =>
        Show(WindowCommand.Focus, snapshot.Key);

    public void Close() =>
        Ask(WindowCommand.Close, null);

    ViewerResponse Handle(ViewerMessage message) =>
        ViewerMessageHandler.Handle(this, message);

    int IQueueOwner.Enqueue(InlinePatch patch)
    {
        int count;
        lock (gate)
        {
            queue = queue.Enqueue(patch);
            count = queue.Count;
        }

        Changed?.Invoke();
        // A patch arriving with no window open starts one; with one, this is the focus.
        Show(WindowCommand.Focus, InlineKey.For(patch.SourceFile, patch.LineHint));
        return count;
    }

    void IQueueOwner.Settle(string key, string? origin, string? member)
    {
        lock (gate)
        {
            var settled = queue.Settle(key, origin, member);
            if (ReferenceEquals(settled, queue))
            {
                return;
            }

            queue = settled;
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Straight into the tracked files, which is where a piper move would have landed. Reaches
    /// this tray when the sending process saw no tray as it started and so addressed the queue
    /// owner instead — and this tray is the queue owner.
    /// </summary>
    void IQueueOwner.TrackMove(string temp, string target)
    {
        TrackedFiles?.AddMove(temp, target);
        Changed?.Invoke();
    }

    void IQueueOwner.TrackDelete(string file)
    {
        TrackedFiles?.AddDelete(file);
        Changed?.Invoke();
    }

    ViewerResponse IQueueOwner.Listing(bool withPatches)
    {
        // Read outside the gate: the tracked collections are concurrent, and only the full
        // listing carries them — the plain listing drives the tray menu, which reads the tracker
        // directly.
        var tracked = withPatches ? TrackedFiles : null;
        var moves = tracked?.Moves();
        var deletes = tracked?.Deletes();
        lock (gate)
        {
            var items = ViewerListing.Items(queue.Items, withPatches);

            // Taken rather than read, so a focus happens once, on whichever viewer refreshes
            // first, rather than five times a second forever.
            //
            // Only for a listing that carries patches, which is the one a viewer asks for. A plain
            // List is the documented IDE plugin API - InlineQueueClient.TryListKeys - and has no
            // window to raise, so handing it the stashed command threw the command away: the
            // attached viewer polling beside it never raised for the new snapshot, and the plugin
            // did nothing with what it was given.
            ViewerResponse response;
            if (withPatches)
            {
                response = ViewerResponse.Listing(items, window, windowKey, moves, deletes);
                window = null;
                windowKey = null;
            }
            else
            {
                response = ViewerResponse.Listing(items, null, null, moves, deletes);
            }

            return response;
        }
    }

    bool IQueueOwner.Has(string key)
    {
        if (TrackedKeys.IsTracked(key))
        {
            return TrackedFiles?.Has(key) ?? false;
        }

        lock (gate)
        {
            return queue.Find(key) is not null;
        }
    }

    (bool ok, string? message) IQueueOwner.Accept(string key, string? origin)
    {
        if (TrackedKeys.IsTracked(key))
        {
            var result = TrackedFiles?.Accept(key) ?? (false, null);
            if (result.ok)
            {
                Changed?.Invoke();
            }

            return result;
        }

        var (outcome, message, refused) = AcceptOne(key, origin);
        if (outcome == AcceptOutcome.Unknown)
        {
            return (false, null);
        }

        if (refused)
        {
            // Nothing changed and nothing was attempted; the message says what a reviewer has to
            // do, and it goes on the wire as an error so a remote surface shows it as one.
            return (false, message);
        }

        Changed?.Invoke();
        return (true, message);
    }

    (bool ok, string? message) IQueueOwner.Discard(string key)
    {
        if (TrackedKeys.IsTracked(key))
        {
            var result = TrackedFiles?.Discard(key) ?? (false, null);
            if (result.ok)
            {
                Changed?.Invoke();
            }

            return result;
        }

        string? message;
        lock (gate)
        {
            if (queue.Find(key) is null)
            {
                return (false, null);
            }

            queue = queue.Discard(key, out message);
        }

        Changed?.Invoke();
        return (true, message);
    }

    /// <summary>
    /// The wire's accept-all sweeps everything this owner shows a viewer: tracked deletes and
    /// moves as well as the snapshots, mirroring the tray menu's own "Accept all". Files first,
    /// the order that menu has always used, and never through <see cref="Tracker.AcceptAll"/>,
    /// whose snapshot half would re-enter this host and whose move path can prompt.
    /// </summary>
    string IQueueOwner.AcceptAll()
    {
        var tracked = TrackedFiles?.AcceptAll();
        var message = AcceptEvery();
        Changed?.Invoke();
        if (tracked is not { } swept ||
            swept is { accepted: 0, kept: 0 })
        {
            return message;
        }

        var clause = swept.kept == 0
            ? $"{swept.accepted} files"
            : $"{swept.accepted} files ({swept.kept} kept)";
        return $"{message}, plus {clause}";
    }

    string IQueueOwner.DiscardAll()
    {
        var tracked = TrackedFiles?.DiscardAll() ?? 0;
        string message;
        lock (gate)
        {
            queue = queue.DiscardAll(out message);
        }

        Changed?.Invoke();
        return tracked == 0 ? message : $"{message}, plus {tracked} files";
    }

    void IQueueOwner.Window(WindowCommand command, string? key)
    {
        // Raising means there has to be something to raise; hiding or closing what is already
        // gone can stay a stash that the next viewer to attach will consume.
        if (command is WindowCommand.Focus or WindowCommand.Show)
        {
            Show(command, key);
        }
        else
        {
            Ask(command, key);
        }
    }

    /// <summary>
    /// Find, apply, complete — with the patch applied outside the gate. Applying waits on
    /// InlineApplier's cross process mutex, up to ten seconds, and everything else here queues
    /// behind the gate: the menu, the listener, every listing the attached viewer polls for.
    /// Completion re-checks the entry, so a re-run that replaced the patch mid apply keeps its
    /// new entry.
    /// <para>
    /// A conflicted entry with no origin to pick is refused before anything is applied: an
    /// un-targeted accept has no honest way to choose a side. Refusals report as
    /// <see cref="AcceptOutcome.Failed"/> so the menu path raises its balloon with the reason.
    /// </para>
    /// </summary>
    (AcceptOutcome outcome, string? message, bool refused) AcceptOne(string key, string? origin)
    {
        PendingInline? entry;
        InlinePatch? patch = null;
        string? refusal = null;
        lock (gate)
        {
            entry = queue.Find(key);
            if (entry is not null)
            {
                if (origin is null)
                {
                    if (entry.Conflicted)
                    {
                        refusal = entry.ConflictRefusal;
                    }
                    else
                    {
                        patch = entry.Patch;
                    }
                }
                else
                {
                    var variant = entry.Variants.FirstOrDefault(_ => _.Origins.Contains(origin));
                    if (variant is null)
                    {
                        refusal = $"No {origin} variant for {entry.Name}";
                    }
                    else
                    {
                        patch = variant.Patch;
                    }
                }
            }
        }

        if (entry is null)
        {
            return (AcceptOutcome.Unknown, null, false);
        }

        if (refusal is not null)
        {
            return (AcceptOutcome.Failed, refusal, true);
        }

        var result = applier(patch!);
        string? message;
        lock (gate)
        {
            queue = queue.Accept(entry, result, out message);
        }

        if (message is null)
        {
            // The completion was ignored: a re-run replaced this call site, or something else
            // took it, while the patch was applying. The patch did reach the file, but what is
            // pending now is a different one, so this outcome describes nothing the caller has.
            return (AcceptOutcome.Unknown, null, false);
        }

        // From the result rather than from whether the entry went, because a stale patch also
        // goes and reporting that as accepted is how "re-run the test" used to get swallowed.
        var outcome = result.Status switch
        {
            InlineApplyStatus.Applied or InlineApplyStatus.AlreadyApplied => AcceptOutcome.Applied,
            InlineApplyStatus.NotFound => AcceptOutcome.Stale,
            _ => AcceptOutcome.Failed
        };
        return (outcome, message, false);
    }

    string AcceptEvery()
    {
        IReadOnlyList<PendingInline> pending;
        lock (gate)
        {
            pending = queue.Items;
        }

        // The list is immutable, so applying over it outside the gate is safe; the completion
        // skips anything that changed underneath. Conflicted entries are never applied: the
        // batch completion counts what it skipped into the message.
        var outcomes = pending
            .Where(_ => !_.Conflicted)
            .Select(_ => (_, applier(_.Patch)))
            .ToList();
        lock (gate)
        {
            queue = queue.AcceptAll(outcomes, out var message);
            return message;
        }
    }

    /// <summary>
    /// Make sure something is displaying the queue, then ask it to do this.
    /// </summary>
    void Show(WindowCommand command, string? key)
    {
        Ask(command, key);
        Launch();
    }

    void Ask(WindowCommand command, string? key)
    {
        lock (gate)
        {
            window = command;
            windowKey = key;
        }
    }

    /// <summary>
    /// One viewer at a time. A second patch while one is up is a focus, not another window.
    /// <para>
    /// The start itself happens outside the gate. Starting a process takes long enough on its own,
    /// and seconds of it with an antivirus between, and every listing the attached viewer polls for
    /// and every patch a test run enqueues was queued behind it. The flag keeps the launcher to one
    /// caller, which is all the gate was doing for it - nothing else reads or writes the launcher.
    /// </para>
    /// </summary>
    void Launch()
    {
        lock (gate)
        {
            if (launcher.Running ||
                launching)
            {
                return;
            }

            launching = true;
        }

        bool started;
        try
        {
            started = launcher.Launch();
        }
        finally
        {
            lock (gate)
            {
                launching = false;
            }
        }

        if (!started)
        {
            failed("Could not start DiffEngineViewer to show the pending snapshots.");
        }
    }

    bool launching;

    public async ValueTask DisposeAsync()
    {
        await cancel.CancelAsync();
        server.Dispose();
        if (listening is null)
        {
            cancel.Dispose();
            Persist();
            return;
        }

        try
        {
            await listening.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (Exception exception)
            when (exception is OperationCanceledException or TimeoutException)
        {
            // Cancellation unwinds through the listener; nothing to report.
        }

        cancel.Dispose();
        Persist();
    }

    /// <summary>
    /// The tray is the durable surface, but exiting it still takes the queue in its memory. What
    /// is still pending goes back to disk in the staging layout, where accept tooling finds it —
    /// the same degradation an exiting owning viewer performs. After the listener has stopped, so
    /// what is written is the final queue.
    /// </summary>
    void Persist() =>
        InlineStaging.Persist(queue.Items);
}
