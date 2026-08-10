/// <summary>
/// Everything the app does apart from choosing a renderer. Each platform head is a
/// <c>Main</c> that supplies its own <see cref="OpenWindow" /> and calls in here, so the queue
/// semantics, the wire protocol and the loop are shared rather than reimplemented per platform.
/// </summary>
static class ViewerProgram
{
    public static int Run(string[] args, OpenWindow open)
    {
        var request = CommandLine.Parse(args);
        if (request.Error is not null)
        {
            Console.Error.WriteLine(request.Error);
            return 2;
        }

        try
        {
            if (request.Attach)
            {
                return RunAttached(open);
            }

            if (request.Mode == ViewerMode.Inline)
            {
                return RunInline(open);
            }

            return RunFile(request, open);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 3;
        }
    }

    static int RunInline(OpenWindow open)
    {
        // Drained before anything slow. OS pipe buffers are around 64 KB, so a parent writing a
        // larger payload blocks on the write until this side reads it, and that parent is a test
        // process that must not hang.
        //
        // Read as UTF8 rather than through Console.In, which decodes using the console code page.
        // A .NET Framework parent writes through Process.StandardInput, whose writer emits a BOM,
        // and under a non UTF8 code page those bytes decode to mojibake rather than a preamble.
        // detectEncodingFromByteOrderMarks strips it.
        using var reader = new StreamReader(
            Console.OpenStandardInput(),
            new UTF8Encoding(false),
            detectEncodingFromByteOrderMarks: true);
        var payload = reader.ReadToEnd();
        if (!InlinePatchFile.TryParse(payload, out var patch))
        {
            Console.Error.WriteLine("Could not read an inline patch payload from stdin.");
            return 2;
        }

        var port = ViewerClient.Port;
        if (!ViewerServer.TryBind(port, out var server))
        {
            // Something else holds the queue, a tray or another viewer, so hand the patch over and
            // get out of the way. Whichever it is will show it.
            var forwarded = ViewerClient.TrySend(new(ViewerVerb.Inline, Body: payload), out _, port);
            if (!forwarded)
            {
                Console.Error.WriteLine("A viewer holds the port but did not accept the patch.");
                return 1;
            }

            return 0;
        }

        using (server)
        {
            var start = ViewerSession.EnqueueInline(SessionState.Start(ViewerMode.Inline), patch);
            return Run(new(start), server, null, open);
        }
    }

    /// <summary>
    /// Display only: the queue belongs to whoever holds the port, and this process just draws it
    /// and forwards commands. Launched this way by DiffEngineTray, which owns the queue itself and
    /// so can never be the window.
    /// </summary>
    static int RunAttached(OpenWindow open)
    {
        var host = new SessionHost(SessionState.Start(ViewerMode.Inline));
        var windowCommands = new ConcurrentQueue<WindowCommand>();
        var link = new OwnerLink(host, ViewerClient.Port, windowCommands.Enqueue);

        // Read once before anything is shown, so an owner that has gone or has nothing pending
        // means no window at all rather than one that closes itself a frame later.
        if (!link.Pump())
        {
            Console.Error.WriteLine("No queue owner is running.");
            return 1;
        }

        if (host.State.Queue.Count == 0)
        {
            return 0;
        }

        return Run(host, null, link, open, windowCommands);
    }

    static int RunFile(ViewerRequest request, OpenWindow open)
    {
        var left = request.Left!;
        var right = request.Right!;
        if (!File.Exists(left))
        {
            Console.Error.WriteLine($"File not found: {left}");
            return 2;
        }

        var entry = QueueEntry.ForFiles(left, right, Read(left), Read(right));
        var start = ViewerSession.EnqueueFile(SessionState.Start(ViewerMode.File), entry);
        return Run(new(start), null, null, open);
    }

    // A missing target is normal: DiffEngine creates an empty one for tools that need it, and a
    // brand new snapshot has nothing on the right yet.
    static string Read(string path) =>
        File.Exists(path) ? File.ReadAllText(path) : "";

    /// <summary>
    /// A non null <paramref name="link"/> means this window is displaying someone else's queue, so
    /// commands that change it are forwarded rather than applied here.
    /// </summary>
    static int Run(
        SessionHost host,
        ViewerServer? server,
        OwnerLink? link,
        OpenWindow open,
        ConcurrentQueue<WindowCommand>? windowCommands = null)
    {
        var window = open("DiffEngineViewer", 1100, 700, false, out var error);
        if (window is null)
        {
            Console.Error.WriteLine(error);
            return 4;
        }

        windowCommands ??= new();
        using var cancel = new CancelSource();
        var listening = server?.Listen(
            new MessageHandler(host, ViewerActions.Real, windowCommands.Enqueue).Handle,
            cancel.Token);
        var polling = link is null
            ? null
            : Task.Run(() => link.Run(cancel.Token), CancellationToken.None);

        using (window)
        {
            Loop(host, window, link, windowCommands);
        }

        cancel.Cancel();
        try
        {
            listening?.Wait(TimeSpan.FromSeconds(2));
            polling?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Cancellation unwinds through both; nothing to report.
        }

        return 0;
    }

    static void Loop(
        SessionHost host,
        IViewerWindow window,
        OwnerLink? link,
        ConcurrentQueue<WindowCommand> windowCommands)
    {
        while (true)
        {
            // Every renderer here is single threaded, so socket driven window changes are applied
            // on this thread rather than on the listener's.
            while (windowCommands.TryDequeue(out var command))
            {
                if (command == WindowCommand.Close)
                {
                    return;
                }

                if (command == WindowCommand.Focus)
                {
                    window.Focus();
                    continue;
                }

                window.SetHidden(command == WindowCommand.Hide);
            }

            var state = host.State;
            if (state.Exit)
            {
                return;
            }

            if (!window.Present(ScreenBuilder.Build(state)))
            {
                return;
            }

            var input = window.Poll();
            host.Mutate(_ => Apply(_, input, link));

            if (!input.CloseRequested)
            {
                continue;
            }

            // With a tray to reopen from, closing hides rather than exits. Without one there is
            // nothing to reopen from, so closing means closing.
            //
            // Hidden rather than exited even when the tray owns the queue and could relaunch:
            // staying up makes reopening a focus rather than a process start, and the tray tracks
            // the process it launched, so it sends that focus instead of starting a second one.
            if (TrayDetector.IsRunning() &&
                host.State.Queue.Count > 0)
            {
                window.SetHidden(true);
                continue;
            }

            return;
        }
    }

    static SessionState Apply(SessionState state, ViewerInput input, OwnerLink? link)
    {
        state = ViewerSession.Resize(state, input.Columns, input.Rows);

        if (input.ScrollDelta != 0)
        {
            var command = input.ScrollDelta > 0 ? CommandKind.ScrollUp : CommandKind.ScrollDown;
            var steps = Math.Min(Math.Abs(input.ScrollDelta) * 3, 30);
            for (var step = 0; step < steps; step++)
            {
                state = ViewerSession.Apply(state, command);
            }
        }

        if (input.ClickedQueueItem >= 0)
        {
            state = ViewerSession.Apply(state, Command.Select(input.ClickedQueueItem));
        }

        if (input.ClickedButton >= 0)
        {
            var buttons = ScreenBuilder.Build(state).Buttons;
            if (input.ClickedButton < buttons.Count)
            {
                var button = buttons[input.ClickedButton];
                if (button.Enabled)
                {
                    state = Dispatch(state, button.Command, link);
                }
            }
        }

        if (input.Key != CommandKind.None)
        {
            state = Dispatch(state, input.Key, link);
        }

        return state;
    }

    /// <summary>
    /// Owning the queue means applying a command here. Displaying someone else's means posting it
    /// to them and letting the next refresh bring the result back, which keeps the round trip and
    /// the ten second mutex behind it off this thread.
    /// </summary>
    static SessionState Dispatch(SessionState state, Command command, OwnerLink? link)
    {
        if (link is null)
        {
            return ViewerSession.Apply(state, command, ViewerActions.Real);
        }

        var verb = Remote(command.Kind);
        if (verb is null)
        {
            return ViewerSession.Apply(state, command);
        }

        // Captured now rather than when it is sent, because selection can move in between.
        var key = verb is ViewerVerb.Accept or ViewerVerb.Discard ? state.Current?.Key : null;
        link.Post(verb.Value, key);
        return state with { Message = "Waiting for the queue owner." };
    }

    static ViewerVerb? Remote(CommandKind kind) =>
        kind switch
        {
            CommandKind.Accept => ViewerVerb.Accept,
            CommandKind.AcceptAll => ViewerVerb.AcceptAll,
            CommandKind.Discard => ViewerVerb.Discard,
            CommandKind.DiscardAll => ViewerVerb.DiscardAll,
            _ => null
        };
}
