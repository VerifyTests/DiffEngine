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
/// A tray restart still loses the queue, exactly as it loses tracked moves and deletes. The
/// recovery is the same: re-run the tests.
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
            return queue.Items
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
        var (outcome, text) = AcceptOne(snapshot.Key);
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

    public void DiscardAll() =>
        ((IQueueOwner) this).DiscardAll();

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

    void IQueueOwner.Settle(string key)
    {
        lock (gate)
        {
            var settled = queue.Settle(key);
            if (ReferenceEquals(settled, queue))
            {
                return;
            }

            queue = settled;
        }

        Changed?.Invoke();
    }

    ViewerResponse IQueueOwner.Listing(bool withPatches)
    {
        lock (gate)
        {
            var items = queue.Items
                .Select(_ => new ViewerResponseItem(
                    _.Key,
                    _.Name,
                    _.Status,
                    withPatches ? InlinePatchFile.Build(_.Patch) : null))
                .ToList();

            // Taken rather than read, so a focus happens once, on whichever viewer refreshes
            // first, rather than five times a second forever.
            var command = window;
            var key = windowKey;
            window = null;
            windowKey = null;
            return ViewerResponse.Listing(items, command, key);
        }
    }

    bool IQueueOwner.Has(string key)
    {
        lock (gate)
        {
            return queue.Find(key) is not null;
        }
    }

    (bool known, string? message) IQueueOwner.Accept(string key)
    {
        var (outcome, message) = AcceptOne(key);
        if (outcome == AcceptOutcome.Unknown)
        {
            return (false, null);
        }

        Changed?.Invoke();
        return (true, message);
    }

    (bool known, string? message) IQueueOwner.Discard(string key)
    {
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

    string IQueueOwner.AcceptAll()
    {
        var message = AcceptEvery();
        Changed?.Invoke();
        return message;
    }

    string IQueueOwner.DiscardAll()
    {
        string message;
        lock (gate)
        {
            queue = queue.DiscardAll(out message);
        }

        Changed?.Invoke();
        return message;
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
    /// </summary>
    (AcceptOutcome outcome, string? message) AcceptOne(string key)
    {
        PendingInline? entry;
        lock (gate)
        {
            entry = queue.Find(key);
        }

        if (entry is null)
        {
            return (AcceptOutcome.Unknown, null);
        }

        var result = applier(entry.Patch);
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
            return (AcceptOutcome.Unknown, null);
        }

        // From the result rather than from whether the entry went, because a stale patch also
        // goes and reporting that as accepted is how "re-run the test" used to get swallowed.
        var outcome = result.Status switch
        {
            InlineApplyStatus.Applied or InlineApplyStatus.AlreadyApplied => AcceptOutcome.Applied,
            InlineApplyStatus.NotFound => AcceptOutcome.Stale,
            _ => AcceptOutcome.Failed
        };
        return (outcome, message);
    }

    string AcceptEvery()
    {
        IReadOnlyList<PendingInline> pending;
        lock (gate)
        {
            pending = queue.Items;
        }

        // The list is immutable, so applying over it outside the gate is safe; the completion
        // skips anything that changed underneath.
        var outcomes = pending
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
    /// </summary>
    void Launch()
    {
        lock (gate)
        {
            if (launcher.Running)
            {
                return;
            }

            if (!launcher.Launch())
            {
                failed("Could not start DiffEngineViewer to show the pending snapshots.");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await cancel.CancelAsync();
        server.Dispose();
        if (listening is null)
        {
            cancel.Dispose();
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
    }
}
