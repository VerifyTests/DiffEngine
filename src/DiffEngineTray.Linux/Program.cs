/// <summary>
/// The tray on Linux. What it does is the Windows head's, in the same order, less what only a
/// window or Windows can do: no hot keys, no options window, and no settings file read, since
/// nothing in it does anything here. Purge asks through zenity or kdialog, and is not on the menu
/// where neither is installed. Nothing kills a process holding a file either, a Unix lock keeping
/// nothing out.
/// </summary>
static class Program
{
    static async Task Main()
    {
        TrayViewerDirectory.Register();
        Logging.Init();
        try
        {
            await Inner();
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

        using var tray = LinuxTray.Open(out var trayError);
        if (tray is null)
        {
            // Run anyway: the queue and the tracked files are most of what the tray is for, and
            // the viewer shows them
            Log.Warning("No tray to put an icon in. {Error}", trayError);
            Warn($"There is nowhere to show DiffEngineTray's menu, so it is holding the snapshot queue and tracking what test runs send it without one. {trayError}");
        }

        if (TrayUpdate.Report() is { } report)
        {
            DesktopNotification.Show("DiffEngineTray", report);
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

        // The SIGTERM and SIGHUP a session ends with, which .NET otherwise ends the process on
        // with nothing below run
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
            Purge: DesktopPurge.For(DesktopDialog.ForPlatform(), Warn),
            OpenDiffTool: DiffToolLauncher.Launch,
            RunAtLogin: RunAtLogin.ForPlatform(TrayLogin.Item),
            Update: TrayUpdate.MenuAction(tokenSource.Cancel));
        try
        {
            await TrayLoop.Run(tray, tracker, actions, () => icon, cancel);
        }
        finally
        {
            TrayVersionFile.Delete();
        }

        await tokenSource.CancelAsync();
        await server;
    }

    /// <summary>
    /// Through the desktop's notifications, which is where the Windows head's balloons go here.
    /// </summary>
    static void Warn(string message) =>
        DesktopNotification.Show("DiffEngineTray", new("DiffEngineTray", message));
}
