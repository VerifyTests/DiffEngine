static class Program
{
    static int Main(string[] args)
    {
        NativeResolver.Register();
        var request = CommandLine.Parse(args);
        if (request.Error is not null)
        {
            Console.Error.WriteLine(request.Error);
            return 2;
        }

        try
        {
            if (request.Mode == ViewerMode.Inline)
            {
                return RunInline();
            }

            return RunFile(request);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 3;
        }
    }

    static int RunInline()
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

        var port = ViewerPort.Resolve();
        if (!ViewerServer.TryBind(port, out var server))
        {
            // Another viewer owns the queue, so hand the patch over and get out of the way.
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
            var start = ViewerSession.Enqueue(
                SessionState.Start(ViewerMode.Inline),
                QueueEntry.ForInline(patch));
            return Run(new(start), server);
        }
    }

    static int RunFile(ViewerRequest request)
    {
        var left = request.Left!;
        var right = request.Right!;
        if (!File.Exists(left))
        {
            Console.Error.WriteLine($"File not found: {left}");
            return 2;
        }

        var entry = QueueEntry.ForFiles(left, right, Read(left), Read(right));
        var start = ViewerSession.Enqueue(SessionState.Start(ViewerMode.File), entry);
        return Run(new(start), null);
    }

    // A missing target is normal: DiffEngine creates an empty one for tools that need it, and a
    // brand new snapshot has nothing on the right yet.
    static string Read(string path) =>
        File.Exists(path) ? File.ReadAllText(path) : "";

    static int Run(SessionHost host, ViewerServer? server)
    {
        if (!ViewerWindow.TryOpen("DiffEngineViewer", 1100, 700, false, out var window, out var error))
        {
            Console.Error.WriteLine(error);
            return 4;
        }

        var actions = ViewerActions.Real;
        var windowCommands = new ConcurrentQueue<WindowCommand>();
        using var cancel = new CancelSource();
        var listening = server?.Listen(
            new MessageHandler(host, actions, windowCommands.Enqueue).Handle,
            cancel.Token);

        using (window)
        {
            Loop(host, window, actions, windowCommands);
        }

        cancel.Cancel();
        try
        {
            listening?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Cancellation unwinds through the listener; nothing to report.
        }

        return 0;
    }

    static void Loop(
        SessionHost host,
        ViewerWindow window,
        ViewerActions actions,
        ConcurrentQueue<WindowCommand> windowCommands)
    {
        while (true)
        {
            // GLFW is single threaded, so socket driven window changes are applied here rather
            // than on the listener's thread.
            while (windowCommands.TryDequeue(out var command))
            {
                var hidden = command == WindowCommand.Hide;
                if (command == WindowCommand.Focus)
                {
                    ViewerWindow.Focus();
                    continue;
                }

                ViewerWindow.SetHidden(hidden);
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

            var input = ViewerWindow.Poll();
            host.Mutate(_ => Apply(_, input, actions));

            if (!input.CloseRequested)
            {
                continue;
            }

            // With a tray to reopen from, closing hides rather than discards. Without one there
            // is nothing to reopen from, so closing means closing.
            if (TrayDetector.IsRunning() &&
                host.State.Queue.Count > 0)
            {
                ViewerWindow.SetHidden(true);
                continue;
            }

            return;
        }
    }

    static SessionState Apply(SessionState state, ViewerInput input, ViewerActions actions)
    {
        state = ViewerSession.Resize(state, input.Columns, input.Rows);

        if (input.ScrollDelta != 0)
        {
            var command = input.ScrollDelta > 0 ? CommandKind.ScrollUp : CommandKind.ScrollDown;
            var steps = Math.Min(Math.Abs(input.ScrollDelta) * 3, 30);
            for (var step = 0; step < steps; step++)
            {
                state = ViewerSession.Apply(state, command, actions);
            }
        }

        if (input.ClickedQueueItem >= 0)
        {
            state = ViewerSession.Apply(state, Command.Select(input.ClickedQueueItem), actions);
        }

        if (input.ClickedButton >= 0)
        {
            var buttons = ScreenBuilder.Build(state).Buttons;
            if (input.ClickedButton < buttons.Count)
            {
                var button = buttons[input.ClickedButton];
                if (button.Enabled)
                {
                    state = ViewerSession.Apply(state, button.Command, actions);
                }
            }
        }

        if (input.Key != CommandKind.None)
        {
            state = ViewerSession.Apply(state, input.Key, actions);
        }

        return state;
    }
}
