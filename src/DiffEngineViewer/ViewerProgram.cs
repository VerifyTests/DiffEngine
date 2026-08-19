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

            if (request.Delete)
            {
                return RunDelete(request.Left!, open);
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
    /// One pending delete, owning the queue so more can join it.
    /// <para>
    /// Launched by DiffEngine when no tray is running and nothing answered on the port. Two
    /// deletes racing both launch, and the loser hands its file to the winner and exits, which is
    /// the same resolution <see cref="RunInline"/> reaches for a second patch.
    /// </para>
    /// </summary>
    static int RunDelete(string file, OpenWindow open)
    {
        var port = ViewerClient.Port;
        if (!ViewerServer.TryBind(port, out var server))
        {
            if (!ViewerClient.TrySend(new(ViewerVerb.Delete, file), out var response, port) ||
                !response.Ok)
            {
                Console.Error.WriteLine("A viewer holds the port but did not accept the delete.");
                return 1;
            }

            return 0;
        }

        using (server)
        {
            var start = ViewerSession.EnqueueTracked(
                SessionState.Start(ViewerMode.Inline),
                TrackedEntry.ForDelete(file));
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
        var link = new OwnerLink(host, ViewerClient.Port);

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

        return Run(host, null, link, open);
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

        // A missing target is normal: DiffEngine creates an empty one for tools that need it, and a
        // brand new snapshot has nothing on the right yet.
        var entry = QueueEntry.ForFiles(left, right, FileSide.Read(left), FileSide.Read(right));
        var start = ViewerSession.EnqueueFile(SessionState.Start(ViewerMode.File), entry);
        return Run(new(start), null, null, open);
    }

    /// <summary>
    /// A non null <paramref name="link"/> means this window is displaying someone else's queue, so
    /// commands that change it are forwarded rather than applied here.
    /// </summary>
    static int Run(SessionHost host, ViewerServer? server, OwnerLink? link, OpenWindow open)
    {
        var window = open("DiffEngineViewer", 1100, 700, false, out var error);
        if (window is null)
        {
            Console.Error.WriteLine(error);
            return 4;
        }

        // Whichever of the two produces them; a process either owns the queue or displays one.
        var windowCommands = link?.Windows ?? new();
        using var cancel = new CancelSource();
        var listening = server?.Listen(
            new MessageHandler(host, ViewerActions.Real, windowCommands.Enqueue).Handle,
            cancel.Token);
        var polling = link is null
            ? null
            : Task.Run(() => link.Run(cancel.Token), Cancel.None);

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

        // After the listener has stopped, so what is written is the final queue.
        PersistOwned(host.State, link);

        return 0;
    }

    /// <summary>
    /// An owning viewer's queue lives in this process's memory, so exiting with entries still
    /// pending used to discard them silently. Staged instead, so accept tooling still finds them
    /// on disk — the arrangement a run with no owner leaves. An attached viewer persists nothing:
    /// the owner it displays is still holding everything.
    /// </summary>
    internal static int PersistOwned(SessionState state, OwnerLink? link)
    {
        if (link is not null)
        {
            return 0;
        }

        return InlineStaging.Persist(
            state.Queue
                .Where(_ => _.Kind == QueueEntryKind.Inline)
                .Select(_ => new PendingInline(_.Variants, _.Status)));
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

            // Q, Escape and the Close menu item arrive as a state flag, consumed here into the
            // same decision as the window's own close button. Routed rather than exited, because
            // quit-as-exit skipped the tray check below: the keyboard threw away an owning
            // viewer's queue in the arrangement where the close button hid the window and kept it.
            var closeRequested = input.CloseRequested;
            if (host.State.QuitRequested)
            {
                closeRequested = true;
                host.Mutate(_ => _ with { QuitRequested = false });
            }

            if (!closeRequested)
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

        if (input.ClickedMenuItem >= 0)
        {
            if (state.Menu is { } open &&
                input.ClickedMenuItem < open.Items.Count)
            {
                state = Dispatch(state, open.Items[input.ClickedMenuItem].Kind, link);
            }
        }
        else if (input.RightClickedQueueItem >= 0)
        {
            state = ViewerSession.OpenMenu(state, input.RightClickedQueueItem);
        }
        else if (input.ClickedQueueItem >= 0)
        {
            // The head reports a row in the drawn column, which the projection maps back to either
            // an entry or a group. Rebuilt the same way the button lookup below rebuilds. Either
            // way a left click closes an open menu.
            var rows = ScreenBuilder.Build(state).Queue;
            var row = input.ClickedQueueItem < rows.Count ? rows[input.ClickedQueueItem] : null;
            if (row?.EntryIndex >= 0)
            {
                state = ViewerSession.Apply(state, Command.Select(row.EntryIndex));
            }
            else if (row?.GroupKey is { } group)
            {
                // A header is the fold control, which is why clicking one is no longer inert.
                state = ViewerSession.ToggleGroup(state, group);
            }
            else if (state.Menu is not null)
            {
                state = state with { Menu = null };
            }
        }

        // After the click chain above, deliberately: that branch needs the menu still open to
        // resolve which item was chosen, so clearing first would swallow the command. And not when
        // a right-click opened another menu in the same frame, which is the dismissal's successor
        // rather than something to undo.
        if (input is {MenuClosed: true, RightClickedQueueItem: < 0} &&
            state.Menu is not null)
        {
            state = state with { Menu = null };
        }

        if (input.ScrollTo >= 0)
        {
            // After the wheel notches, so an absolute target wins over a delta in the same frame.
            state = ViewerSession.Apply(state, Command.Scroll(input.ScrollTo));
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

        // Local even when displaying someone else's queue: revealing reads this machine's disk,
        // which is where the files are, because the protocol never leaves the machine.
        if (command.Kind == CommandKind.RevealSource)
        {
            return ViewerSession.Apply(state, command, ViewerActions.Real);
        }

        if (command.Kind is CommandKind.AcceptGroup or CommandKind.DiscardGroup)
        {
            return DispatchGroup(state, command.Kind, link);
        }

        var verb = Remote(command.Kind);
        if (verb is null)
        {
            return ViewerSession.Apply(state, command);
        }

        // Captured now rather than when it is sent, because selection can move in between.
        var key = verb is ViewerVerb.Accept or ViewerVerb.Discard ? state.Current?.Key : null;
        // Accepting a conflicted entry names the variant on screen, so the owner applies exactly
        // what the reviewer was reading.
        string? body = null;
        if (verb is ViewerVerb.Accept &&
            state.Current is { Kind: QueueEntryKind.Inline, Conflicted: true } current &&
            current.Variants[current.SelectedVariant].Origins is { Count: > 0 } origins)
        {
            body = origins[0];
        }

        link.Post(verb.Value, key, body);
        return state with
        {
            Message = "Waiting for the queue owner.",
            Menu = null
        };
    }

    /// <summary>
    /// A group command against someone else's queue: one accept or discard per member, by key,
    /// with conflicted entries skipped the way every bulk accept skips them. The results come
    /// back on the next listing like any other forwarded command.
    /// </summary>
    static SessionState DispatchGroup(SessionState state, CommandKind kind, OwnerLink link)
    {
        if (state.Menu is not { } menu)
        {
            return state;
        }

        foreach (var index in menu.Members)
        {
            if (index < 0 ||
                index >= state.Queue.Count)
            {
                continue;
            }

            var entry = state.Queue[index];
            if (kind == CommandKind.AcceptGroup)
            {
                if (!entry.Conflicted)
                {
                    link.Post(ViewerVerb.Accept, entry.Key);
                }

                continue;
            }

            link.Post(ViewerVerb.Discard, entry.Key);
        }

        return state with
        {
            Message = "Waiting for the queue owner.",
            Menu = null
        };
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
