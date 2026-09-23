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

            if (request.Diff)
            {
                return RunDiff(request.Left!, request.Right!, open);
            }

            if (request.Mode == ViewerMode.Inline)
            {
                return RunInline(request.Payload, open);
            }

            return RunFile(request, open);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 3;
        }
    }

    static int RunInline(string? payloadFile, OpenWindow open)
    {
        var payload = ReadPayload(payloadFile);
        if (payload is null ||
            !InlinePatchFile.TryParse(payload, out var patch))
        {
            Console.Error.WriteLine("Could not read an inline patch payload.");
            return 2;
        }

        var port = ViewerClient.Port;
        if (!ViewerServer.TryBind(port, out var server))
        {
            // Something else holds the queue, a tray or another viewer, so hand the patch over and
            // get out of the way. Whichever it is will show it.
            if (!ViewerClient.TrySend(new(ViewerVerb.Inline, Body: payload), out var response, port) ||
                !response.Ok)
            {
                // Refused - an owner on its way out, or one too old for the payload - or gone
                // between the bind and the send. Whoever launched this was told the patch was
                // taken, and a refusal used to be read as a hand over, so it is staged rather
                // than dropped: this process is the only place it exists.
                InlineStaging.Persist([new PendingInline(patch)]);
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
    /// The patch a launch handed over: from the file it wrote, which is then deleted, or from
    /// stdin, which is how a DiffEngine from before <see cref="ViewerRequest.Payload" /> sends it.
    /// Null when the file cannot be read.
    /// <para>
    /// Read before anything slow either way. OS pipe buffers are around 64 KB, so a parent writing
    /// a larger payload to stdin blocks on the write until this side reads it, and that parent is a
    /// test process that must not hang.
    /// </para>
    /// <para>
    /// Read as UTF8 rather than through Console.In, which decodes using the console code page. A
    /// .NET Framework parent writes through Process.StandardInput, whose writer emits a BOM, and
    /// under a non UTF8 code page those bytes decode to mojibake rather than a preamble.
    /// detectEncodingFromByteOrderMarks strips it.
    /// </para>
    /// </summary>
    internal static string? ReadPayload(string? payloadFile)
    {
        if (payloadFile is null)
        {
            using var reader = new StreamReader(
                Console.OpenStandardInput(),
                new UTF8Encoding(false),
                detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        try
        {
            return File.ReadAllText(payloadFile, new UTF8Encoding(false));
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            // Whether or not it read: the file exists only to carry this one patch across the
            // launch, and this is the only process that knows it is finished with it
            try
            {
                File.Delete(payloadFile);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
            }
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
    /// One failing pair, owning the queue so more can join it.
    /// <para>
    /// Launched by DiffEngine when the diff tool it resolved for the pair is this viewer and
    /// nothing answered on the port. Tracked as a pending move, which is what the pair already
    /// is everywhere else: the same entry a tray's move produces, accepted by putting the
    /// received file where the target is.
    /// </para>
    /// <para>
    /// Deliberately not <see cref="ViewerMode.File"/>, which owns no port and so cannot be joined.
    /// That mode is what a hand run <c>DiffEngineViewer left right</c> still gets, where a queue
    /// nothing else can add to is the whole intent.
    /// </para>
    /// </summary>
    static int RunDiff(string temp, string target, OpenWindow open)
    {
        var port = ViewerClient.Port;
        if (!ViewerServer.TryBind(port, out var server))
        {
            if (!ViewerClient.TrySend(new(ViewerVerb.Diff, temp, target), out var response, port) ||
                !response.Ok)
            {
                Console.Error.WriteLine("A viewer holds the port but did not accept the pair.");
                return 1;
            }

            return 0;
        }

        using (server)
        {
            var start = ViewerSession.EnqueueTracked(
                SessionState.Start(ViewerMode.Inline),
                TrackedEntry.ForMove(temp, target));
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
    /// commands that change it are forwarded rather than applied here. Internal so
    /// ViewerProgramTests can hand it a window that will not open, or one that throws.
    /// </summary>
    internal static int Run(SessionHost host, ViewerServer? server, OwnerLink? link, OpenWindow open)
    {
        var window = open("DiffEngineViewer", 1100, 700, false, out var error);
        if (window is null)
        {
            Console.Error.WriteLine(error);
            // The port was bound before the window was asked for, so whoever launched this saw an
            // owner and was told what it sent had been taken - and that is only in this process's
            // memory. Staged instead, where accept tooling finds it. A display that is not there
            // or a native library that will not load are both ordinary on Linux, and each used to
            // cost every inline snapshot of the run.
            PersistOwned(host.State, link);
            return 4;
        }

        if (window is ILoopHooks hooks)
        {
            hooks.Frame = () => ModalFrame(host, window, link);
            hooks.SessionEnding = () =>
            {
                host.Mutate(_ => _ with { Closing = true });
                PersistOwned(host.State, link);
            };
        }

        // Whichever of the two produces them; a process either owns the queue or displays one.
        var windowCommands = link?.Windows ?? new();
        using var cancel = new CancelSource();
        // Only for a queue this process owns. A displayed one's accept-all runs in its owner, and
        // file mode's is the one comparison it shows.
        var runner = server is null ? null : new AcceptAllRunner(host, ViewerActions.Real);
        var listening = server?.Listen(
            new MessageHandler(host, ViewerActions.Real, windowCommands.Enqueue, runner).Handle,
            cancel.Token);
        var polling = link is null
            ? null
            : Task.Run(() => link.Run(cancel.Token), Cancel.None);
        // Only for a queue this process owns. A displayed one is re-read by OwnerLink already, and
        // its files belong to the owner, which is what decides when an entry stops being pending.
        var watching = server is null
            ? null
            : Task.Run(() => new TrackedWatch(host).Run(cancel.Token), Cancel.None);

        // Finally, so a loop that throws still ends the way one that returns does. The throw used
        // to unwind straight past all of this to Main's catch, and the queue went with it.
        try
        {
            using (window)
            {
                Loop(host, window, link, windowCommands, runner);
            }
        }
        finally
        {
            // However the loop ended, nothing arriving from here on has a window to be shown in,
            // and the listener keeps answering until it is cancelled below
            host.Mutate(_ => _ with { Closing = true });

            // Closing the window mid batch does not abandon it: clicking Accept all and then
            // closing used to mean both happened, because the click held the window until it was
            // done. Before the listener stops, so a drive the tray started finishes answering it.
            runner?.Finish();

            cancel.Cancel();
            try
            {
                listening?.Wait(TimeSpan.FromSeconds(2));
                polling?.Wait(TimeSpan.FromSeconds(2));
                watching?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
                // Cancellation unwinds through both; nothing to report.
            }

            // After the listener has stopped, so what is written is the final queue.
            PersistOwned(host.State, link);
        }

        return 0;
    }

    /// <summary>
    /// A frame from inside a head's modal loop (<see cref="ILoopHooks.Frame"/>): what the loop does
    /// with input, without the present, which the head is already inside of.
    /// </summary>
    static Screen ModalFrame(SessionHost host, IViewerWindow window, OwnerLink? link)
    {
        var state = host.State;
        var input = window.Poll();
        if (!IsIdle(input, state))
        {
            state = host.Mutate(_ => Apply(_, input, link, window));
        }

        return ScreenBuilder.Build(state);
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
        ConcurrentQueue<WindowCommand> windowCommands,
        AcceptAllRunner? runner)
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

                var hide = command == WindowCommand.Hide;
                // A focus shows the window as well as raising it
                link?.Hidden = hide;

                if (command == WindowCommand.Focus)
                {
                    window.Focus();
                    continue;
                }

                window.SetHidden(hide);
            }

            // Committed under the lock rather than read and acted on. Between reading Exit and the
            // listener stopping, an arrival used to be answered as queued and then leave with the
            // window. One landing first clears Exit and keeps the window open; one landing after
            // finds the viewer closing and is refused, so its sender stages it instead.
            if (host.State.Exit &&
                host.Mutate(ViewerSession.CommitExit).Closing)
            {
                return;
            }

            var state = host.State;
            if (!window.Present(ScreenBuilder.Build(state)))
            {
                return;
            }

            var input = window.Poll();
            // Not on a frame with nothing in it, which is almost all of them. The listener thread
            // takes the same lock to accept a snapshot, which can wait ten seconds on
            // InlineApplier's mutex, and taking it every frame put the render loop behind that
            // wait - the stall SessionHost's lock free reads exist to prevent.
            if (!IsIdle(input, state))
            {
                host.Mutate(_ => Apply(_, input, link, window));
            }

            // An accept-all this frame's input began is carried out on a worker, so this thread
            // goes back to drawing the queue as it shrinks. One the tray began is already being
            // carried out by the listener thread, and the runner leaves that one to it.
            if (runner is not null &&
                host.State.Batch is not null)
            {
                runner.Start();
            }

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
            //
            // Never in file mode, which owns no port and which the tray does not know about:
            // nothing could ever show it again, and a caller blocked on the process waited forever.
            if (host.State.Mode == ViewerMode.Inline &&
                TrayDetector.IsRunning() &&
                host.State.Queue.Count > 0)
            {
                window.SetHidden(true);
                link?.Hidden = true;

                continue;
            }

            return;
        }
    }

    /// <summary>
    /// A frame that would change nothing: no key, click, scroll, drag or close, and the window
    /// the size the state already is.
    /// </summary>
    internal static bool IsIdle(ViewerInput input, SessionState state) =>
        input.Key == CommandKind.None &&
        input.ClickedButton < 0 &&
        input.ClickedQueueItem < 0 &&
        input.ScrollDelta == 0 &&
        !input.CloseRequested &&
        input.RightClickedQueueItem < 0 &&
        input.ClickedMenuItem < 0 &&
        !input.MenuClosed &&
        input.ScrollTo < 0 &&
        input.DragSide < 0 &&
        Math.Max(40, input.Columns) == state.Columns &&
        Math.Max(10, input.Rows) == state.Rows;

    /// <summary>
    /// One frame of input against one state. Internal so SelectionTests can drive a drag and a
    /// copy the way a head does, since the clipboard and the drag are only connected here.
    /// </summary>
    internal static SessionState Apply(SessionState state, ViewerInput input, OwnerLink? link, IViewerWindow window)
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
                state = Dispatch(state, open.Items[input.ClickedMenuItem].Kind, link, window);
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

        // After the click chain, deliberately. A drag closes the menu like every other input, and
        // the head that draws its own menu reports a click on one as landing wherever the menu is
        // floating - which is over a pane. Resolving the drag first would then close the menu
        // before the branch above could look up which item was chosen, and swallow the command.
        //
        // Order against the scroll does not matter: both ends arrive in rows of the whole side,
        // which is what a scroll top is subtracted from rather than added to.
        if (input.DragSide >= 0)
        {
            state = ViewerSession.Drag(
                state,
                input.DragSide == 0 ? PaneSide.Left : PaneSide.Right,
                input.DragAnchorRow,
                input.DragAnchorColumn,
                input.DragFocusRow,
                input.DragFocusColumn);
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
                    state = Dispatch(state, button.Command, link, window);
                }
            }
        }

        if (input.Key != CommandKind.None)
        {
            state = Dispatch(state, input.Key, link, window);
        }

        return state;
    }

    /// <summary>
    /// Owning the queue means applying a command here. Displaying someone else's means posting it
    /// to them and letting the next refresh bring the result back, which keeps the round trip and
    /// the ten second mutex behind it off this thread.
    /// </summary>
    static SessionState Dispatch(SessionState state, Command command, OwnerLink? link, IViewerWindow window)
    {
        // Before everything, including the link check. Copying reads what is on screen and writes
        // it to this machine's clipboard, so it is never something to ask an owner for - and the
        // owner's answer would be the text this process already has.
        if (command.Kind is CommandKind.Copy or CommandKind.CopyLeft or CommandKind.CopyRight)
        {
            return Copy(state, command.Kind, window);
        }

        // Nothing that changes the queue while an accept-all is working through it, whichever
        // process is running it. The buttons already say so by being disabled; this is the keys
        // and the menu agreeing with them. Looking around - scrolling, selecting, folding, copying
        // - carries on, since that is what there is to do while it runs.
        if (state.Progress is not null &&
            ViewerSession.ChangesQueue(command.Kind))
        {
            return state with { Menu = null };
        }

        if (link is null)
        {
            // Only begun here. Applying every entry inside this frame held the window for as long
            // as the queue was long; the loop hands the batch to a worker instead.
            if (command.Kind == CommandKind.AcceptAll &&
                state.Mode == ViewerMode.Inline)
            {
                return ViewerSession.BeginAcceptAll(state);
            }

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
    /// Pane text to the clipboard, and a status line saying what went. An empty side or an empty
    /// selection says so rather than silently putting nothing on the clipboard, since the two are
    /// indistinguishable afterwards.
    /// </summary>
    static SessionState Copy(SessionState state, CommandKind kind, IViewerWindow window)
    {
        if (state.Current is not { } current)
        {
            return state with { Menu = null };
        }

        string text;
        string what;
        if (kind == CommandKind.Copy)
        {
            if (state.LiveSelection is not { IsEmpty: false } selection)
            {
                return state with
                {
                    Message = "Nothing is selected. Drag across a pane, or press ctrl+a.",
                    Menu = null
                };
            }

            text = SelectionText.Of(selection, current);
            what = "the selection";
        }
        else
        {
            var side = kind == CommandKind.CopyLeft ? PaneSide.Left : PaneSide.Right;
            text = SelectionText.All(current, side);
            what = SelectionText.Header(current, side);
        }

        if (text.Length == 0)
        {
            return state with
            {
                Message = $"Nothing to copy from {what}.",
                Menu = null
            };
        }

        window.SetClipboard(text);
        var lines = text.Count(_ => _ == '\n') + 1;
        return state with
        {
            Message = $"Copied {lines} line{(lines == 1 ? "" : "s")} from {what}.",
            Menu = null
        };
    }

    /// <summary>
    /// A group command against someone else's queue, by key, with conflicted entries skipped the
    /// way every bulk accept skips them. The results come back on the next listing like any other
    /// forwarded command.
    /// <para>
    /// A discard is one per member. An accept goes as one ordered step, deletes last and only once
    /// the snapshots have landed: see <see cref="OwnerLink.PostAcceptGroup"/>.
    /// </para>
    /// </summary>
    static SessionState DispatchGroup(SessionState state, CommandKind kind, OwnerLink link)
    {
        if (state.Menu is not { } menu)
        {
            return state;
        }

        var members = menu.Members
            .Where(_ => _ >= 0 && _ < state.Queue.Count)
            .Select(_ => state.Queue[_])
            .ToList();
        if (kind == CommandKind.AcceptGroup)
        {
            link.PostAcceptGroup(
                KeysOf(members, QueueEntryKind.Move),
                KeysOf(members.Where(_ => !_.Conflicted), QueueEntryKind.Inline),
                KeysOf(members, QueueEntryKind.Delete));
        }
        else
        {
            foreach (var entry in members)
            {
                link.Post(ViewerVerb.Discard, entry.Key);
            }
        }

        return state with
        {
            Message = "Waiting for the queue owner.",
            Menu = null
        };
    }

    static List<string> KeysOf(IEnumerable<QueueEntry> entries, QueueEntryKind kind) =>
        entries
            .Where(_ => _.Kind == kind)
            .Select(_ => _.Key)
            .ToList();

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
