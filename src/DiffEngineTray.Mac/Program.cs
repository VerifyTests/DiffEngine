/// <summary>
/// The tray on macOS. What it does is the Linux head's, in the same order, with the status bar
/// item in place of a StatusNotifierItem.
/// <para>
/// Synchronous, and blocking on <see cref="Inner" />, so the loop runs on the thread Main starts on,
/// which is the process's main thread: AppKit has to be driven from there. Nothing in
/// <see cref="Inner" /> awaits until the loop has returned, so it runs there too.
/// </para>
/// </summary>
static class Program
{
    static void Main()
    {
        TrayViewerDirectory.Register();
        Logging.Init();
        try
        {
            Inner().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Log.Logger.Fatal(exception, "Failed at startup");
            throw;
        }
    }

    static async Task Inner()
    {
        using var tokenSource = new CancelSource();
        var cancel = tokenSource.Token;
        // One a session, as on Windows. A tray in another session is the one holding the port,
        // which this one then warns about
        using var mutex = new Mutex(true, "DiffEngine", out var createdNew);
        if (!createdNew)
        {
            Log.Information("Mutex already exists. Exiting.");
            return;
        }

        try
        {
            TrayVersionFile.Write(VersionReader.VersionString);
        }
        catch (Exception exception)
        {
            // The marker only gates inline snapshot payloads; the tray must still start
            Log.Error(exception, "Failed to write the tray version marker");
        }

        using var tray = MacTray.Open(out var trayError);
        if (tray is null)
        {
            // Run anyway, as the Linux head does with no tray host: the queue and the tracked
            // files are most of what the tray is for, and the viewer shows them
            Log.Warning("No status item. {Error}", trayError);
            Warn($"DiffEngineTray could not put its icon in the menu bar, so it is holding the snapshot queue and tracking what test runs send it without one. {trayError}");
        }

        // Ownership of the inline queue is decided here, once, by whether the bind succeeds, and
        // never transfers. See the Windows head's Program
        await using var owned = OwnedInlineHost.TryOwn(Warn);
        if (owned is null)
        {
            Log.Information("A viewer owns the inline queue. Driving it remotely.");
        }

        var icon = TrayIcon.Default;
        await using var tracker = new Tracker(
            active: () => icon = TrayIcon.Active,
            inactive: () => icon = TrayIcon.Default,
            lockedFilesResolver: null,
            acceptFailed: move => Warn(
                $"Could not accept '{move.Name}': the file move keeps failing. The move is still pending, so accept can be retried."),
            inlineFailed: Warn,
            inline: owned,
            scanFailing: Warn);

        if (owned is not null)
        {
            owned.Changed = tracker.Refresh;
            owned.TrackedFiles = tracker;
            owned.Start();
        }

        // A logout reaches the loop as a quit (MacTray.Quitting), and ends it the way Exit does.
        // These are a session's processes being ended otherwise, which .NET answers by ending the
        // process with nothing below run
        using var sessionEnd = new SessionEndSignals(
            TraySessionEnd.For(owned, TrayVersionFile.Delete),
            _ => Log.Error(_, "Failed while the session was ending"));

        var listener = PiperServer.TryBind(out var bindError);
        if (listener is null)
        {
            Log.Error(bindError, "Could not listen on port {Port}", PiperClient.Port);
            Warn($"Could not listen on port {PiperClient.Port}, so moves and deletes from test runs will not reach the tray. {bindError!.Message}");
        }

        var server = listener is null ? Task.CompletedTask : PiperServer.Serve(listener, tracker, cancel);

        var actions = new TrayActions(
            Exit: tokenSource.Cancel,
            Options: null,
            DebugView: () => DebugReportFile.Open(tracker),
            Purge: null,
            OpenDiffTool: DiffToolLauncher.Launch,
            RunAtLogin: RunAtLogin.ForPlatform(TrayLogin.Item));
        try
        {
            if (tray is null)
            {
                await TrayLoop.Run(null, tracker, actions, () => icon, cancel);
            }
            else
            {
                TrayLoop.RunPumped(
                    tray,
                    () =>
                    {
                        MacTray.Pump();
                        if (MacTray.Quitting)
                        {
                            tokenSource.Cancel();
                        }
                    },
                    tracker,
                    actions,
                    () => icon,
                    cancel);
            }
        }
        finally
        {
            TrayVersionFile.Delete();
        }

        await tokenSource.CancelAsync();
        await server;
    }

    /// <summary>
    /// Through macOS's notifications, by osascript, which is where the Windows head's balloons go
    /// here.
    /// </summary>
    static void Warn(string message) =>
        DesktopNotification.Show("DiffEngineTray", new("DiffEngineTray", message));
}
