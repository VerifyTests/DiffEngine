/// <summary>
/// The tray holding the inline queue itself: the socket every process talks over, the queue, and
/// the viewer launched to display it.
/// <para>
/// The tray is the durable surface. It starts at login and outlives every viewer, so a viewer that
/// crashes or is killed no longer takes the pending snapshots with it, and closing the window
/// stops meaning "keep a 52 MB process resident purely as a data store".
/// </para>
/// <para>
/// Accepting runs here, on a listener thread. That matters: InlineApplier takes a cross process
/// mutex and can wait ten seconds for it, and everywhere else it could have run is a UI or render
/// thread that has to keep pumping.
/// </para>
/// <para>
/// A tray restart still loses the queue, exactly as it loses tracked moves and deletes. The
/// recovery is the same: re-run the tests.
/// </para>
/// </summary>
sealed class OwnedInlineHost :
    IInlineHost,
    IAsyncDisposable
{
    readonly ViewerServer server;
    readonly CancelSource cancel = new();
    readonly Task listening;
    readonly Action<string> failed;
    readonly IViewerLauncher launcher;
    readonly Lock gate = new();
    InlineQueue queue = InlineQueue.Empty;
    WindowCommand? window;
    string? windowKey;

    OwnedInlineHost(ViewerServer server, Action<string> failed, IViewerLauncher launcher)
    {
        this.server = server;
        this.failed = failed;
        this.launcher = launcher;
        listening = server.Listen(Handle, cancel.Token);
    }

    /// <summary>
    /// Null when the bind failed, which means a viewer started first and owns the queue for as
    /// long as it runs. The caller falls back to <see cref="RemoteInlineHost"/>.
    /// <para>
    /// <paramref name="port"/> and <paramref name="launcher"/> are for tests, which need an
    /// ephemeral port so a live tray on the machine is not in the way, and no real window.
    /// </para>
    /// </summary>
    public static OwnedInlineHost? TryOwn(
        Action<string> failed,
        IViewerLauncher? launcher = null,
        int? port = null) =>
        ViewerServer.TryBind(port ?? ViewerClient.Port, out var server)
            ? new(server, failed, launcher ?? new ProcessViewerLauncher())
            : null;

    public int Port => server.Port;

    /// <summary>
    /// Raised when the queue changes from the socket rather than from the menu, so the icon lights
    /// up as a snapshot arrives instead of up to one scan later.
    /// </summary>
    public Action? Changed { get; set; }

    public IReadOnlyList<PendingSnapshot> List()
    {
        lock (gate)
        {
            return queue.Items
                .Select(_ => new PendingSnapshot(_.Key, _.Name, _.Status))
                .ToList();
        }
    }

    public bool Accept(PendingSnapshot snapshot, out string? message)
    {
        lock (gate)
        {
            // Gone means applied, already applied, or a patch too stale to ever apply. A failure
            // keeps its entry and says why, so nothing is lost either way.
            var before = queue.Count;
            queue = queue.Accept(snapshot.Key, InlineApplier.Apply, out message);
            return queue.Count < before;
        }
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
        lock (gate)
        {
            queue = queue.AcceptAll(InlineApplier.Apply, out var outcome);
            message = outcome;
            return queue.Count == 0;
        }
    }

    public void Focus(PendingSnapshot snapshot) =>
        Show(WindowCommand.Focus, snapshot.Key);

    public void Close() =>
        Ask(WindowCommand.Close, null);

    ViewerResponse Handle(ViewerMessage message)
    {
        switch (message.Verb)
        {
            case ViewerVerb.Inline:
                return Inline(message.Body);
            case ViewerVerb.Settle:
                return Settle(message.Key);
            case ViewerVerb.List:
                return Listing(false);
            case ViewerVerb.ListFull:
                return Listing(true);
            case ViewerVerb.Accept:
                return One(message.Key, ViewerVerb.Accept);
            case ViewerVerb.Discard:
                return One(message.Key, ViewerVerb.Discard);
            case ViewerVerb.AcceptAll:
                return Every(ViewerVerb.AcceptAll);
            case ViewerVerb.DiscardAll:
                return Every(ViewerVerb.DiscardAll);
            case ViewerVerb.Focus:
                Show(WindowCommand.Focus, message.Key);
                return ViewerResponse.Success();
            case ViewerVerb.Show:
                Show(WindowCommand.Show, null);
                return ViewerResponse.Success();
            case ViewerVerb.Hide:
                Ask(WindowCommand.Hide, null);
                return ViewerResponse.Success();
            case ViewerVerb.Quit:
                Ask(WindowCommand.Close, null);
                return ViewerResponse.Success("Closing");
            default:
                return ViewerResponse.Error($"Unsupported verb: {message.Verb}");
        }
    }

    ViewerResponse Inline(string? body)
    {
        if (body is null)
        {
            return ViewerResponse.Error("Inline requires a body");
        }

        if (!InlinePatchFile.TryParse(body, out var patch))
        {
            return ViewerResponse.Error("Inline body is not a readable patch payload");
        }

        // Remove strips a literal when inline is switched off. That is a configuration change with
        // nothing to review, so the sender applies it directly rather than queueing it here.
        if (patch.Mode == InlinePatchMode.Remove)
        {
            return ViewerResponse.Error($"{InlinePatchMode.Remove} patches are not reviewable");
        }

        int count;
        lock (gate)
        {
            queue = queue.Enqueue(patch);
            count = queue.Count;
        }

        Changed?.Invoke();
        Show(WindowCommand.Focus, InlineKey.For(patch.SourceFile, patch.LineHint));
        return ViewerResponse.Success($"Queued {count}");
    }

    ViewerResponse Settle(string? key)
    {
        if (key is null)
        {
            return ViewerResponse.Error("Settle requires a key");
        }

        lock (gate)
        {
            var settled = queue.Settle(key);
            if (ReferenceEquals(settled, queue))
            {
                return ViewerResponse.Success();
            }

            queue = settled;
        }

        Changed?.Invoke();
        return ViewerResponse.Success();
    }

    ViewerResponse Listing(bool withPatches)
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

    ViewerResponse One(string? key, ViewerVerb verb)
    {
        if (key is null)
        {
            return ViewerResponse.Error($"{verb} requires a key");
        }

        string? message;
        lock (gate)
        {
            if (queue.Find(key) is null)
            {
                return ViewerResponse.Error($"No pending snapshot for {key}");
            }

            queue = verb == ViewerVerb.Accept
                ? queue.Accept(key, InlineApplier.Apply, out message)
                : queue.Discard(key, out message);
        }

        Changed?.Invoke();
        return ViewerResponse.Success(message);
    }

    ViewerResponse Every(ViewerVerb verb)
    {
        string message;
        lock (gate)
        {
            queue = verb == ViewerVerb.AcceptAll
                ? queue.AcceptAll(InlineApplier.Apply, out message)
                : queue.DiscardAll(out message);
        }

        Changed?.Invoke();
        return ViewerResponse.Success(message);
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
