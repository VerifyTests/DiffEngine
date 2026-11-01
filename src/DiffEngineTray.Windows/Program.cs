static class Program
{
    /// <summary>
    /// Synchronous, so that the attribute is on the method the runtime starts. For an
    /// <c>async Task Main</c> that is a method the compiler writes, which carries none, and the
    /// thread every window here lives on came up MTA: Clipboard.SetText throws ThreadStateException
    /// there, so "Copy" in the debug view never copied anything.
    /// <para>
    /// Blocking on <see cref="Inner"/> is what that compiler written method did as well. Nothing
    /// in it awaits until <c>Application.Run()</c> has returned, by which time WinForms has taken
    /// its synchronization context back off this thread, so what follows continues on the pool
    /// rather than waiting on a loop that is no longer pumping.
    /// </para>
    /// </summary>
    [STAThread]
    static void Main()
    {
        IssueLauncher.Declined = IssueQuestion.Declined;
        TrayViewerDirectory.Register();
        Logging.Init();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

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
        var tokenSource = new CancelSource();
        var cancel = tokenSource.Token;
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

        // Before the settings file rather than after it, because nothing between here and the
        // first use of a setting needs one, and until this line the user is looking at an empty
        // notification area. Still after the mutex, so a second instance exits without ever
        // showing an icon.
        // The tips are those of the menus last shown, which the turns below hold, and are asked
        // for only as a menu is built, by when there are turns to ask
        TrayTurns? turns = null;
        using var tray = NotifyIconTray<TrayIcon>.Open(MenuBuilder.Options(_ => turns?.Tooltip(_)), out var trayError) ??
                         throw new InvalidOperationException($"Could not make the tray icon. {trayError}");
        // A picture now rather than at the first turn, which waits on everything below
        tray.Apply(new(TrayIcon.Default, "DiffEngineTray", []));

        void Warn(string message) =>
            tray.Notify(new("DiffEngineTray", message, Kind: TrayNotificationKind.Warning));

        if (TrayUpdate.Report() is { } report)
        {
            // A failure as a warning, as every balloon of this tray's is
            tray.Notify(report.Kind == TrayNotificationKind.Info ? report : report with {Kind = TrayNotificationKind.Warning});
        }

        var settings = GetSettings();
        if (settings == null)
        {
            return;
        }

        LockedFilesPolicy.AlwaysKill = settings.AlwaysKillLockingProcesses;

        // Ownership of the inline queue is decided here, once, by whether the bind succeeds, and
        // never transfers. Usually the tray wins, because it starts at login. A viewer that was
        // already running keeps the queue for as long as it lives, and this tray drives it
        // remotely for the rest of its own life rather than trying to take over mid flight.
        await using var owned = OwnedInlineHost.TryOwn(Warn);
        if (owned is null)
        {
            Log.Information("A viewer owns the inline queue. Driving it remotely.");
        }

        var icon = TrayIcon.Default;
        await using var tracker = new Tracker(
            active: () => icon = TrayIcon.Active,
            inactive: () => icon = TrayIcon.Default,
            lockedFilesResolver: LockedFilesHandler.Resolve,
            acceptFailed: move => Warn(
                $"Could not accept '{move.Name}': the file move keeps failing. The move is still pending, so accept can be retried."),
            inlineFailed: Warn,
            inline: owned,
            scanFailing: Warn);

        // Owning the queue means knowing the moment it changes, rather than finding out on the
        // next two second scan. Wired before serving starts, so the first patch to arrive counts.
        if (owned is not null)
        {
            owned.Changed = tracker.Refresh;
            owned.TrackedFiles = tracker;
            owned.Start();
        }

        // A logoff or a shutdown does not come back through Application.Run(), so nothing the
        // unwind below performs happens for one
        using var sessionEnd = new SessionEndWindow(
            TraySessionEnd.For(owned, TrayVersionFile.Delete),
            _ => Log.Error(_, "Failed while the session was ending"));

        // Not a using. Anything throwing between here and the await below would dispose a task
        // that is still running, and Task.Dispose throws for one that has not completed - which
        // would replace whatever actually went wrong with an InvalidOperationException. A task
        // needs no disposal anyway; cancelling it is what ends it
        var listener = PiperServer.TryBind(out var bindError);
        if (listener is null)
        {
            Log.Error(bindError, "Could not listen on port {Port}", PiperClient.Port);
            Warn($"Could not listen on port {PiperClient.Port}, so moves and deletes from test runs will not reach the tray. {bindError!.Message}");
        }

        var task = listener is null ? Task.CompletedTask : PiperServer.Serve(listener, tracker, cancel);

        using var keyRegister = new KeyRegister(tray.Handle);
        ReBindKeys(settings, keyRegister, tracker, Warn);

        turns = new(
            tray,
            tracker,
            new(
                Exit: Application.Exit,
                Options: async () => await OptionsFormLauncher.Launch(keyRegister, tracker),
                DebugView: async () => await DebugFormLauncher.Launch(tracker),
                Purge: FilePurger.Launch,
                OpenDiffTool: DiffToolLauncher.Launch),
            () => icon);

        // The tray's events are raised on this thread as Application.Run pumps it, and what they
        // chose is done here too, a turn at a time, as the other heads' loops do
        using var timer = new System.Windows.Forms.Timer
        {
            Interval = (int) TrayLoop.Turn.TotalMilliseconds
        };
        timer.Tick += (_, _) => turns.Turn();
        timer.Start();

        try
        {
            Application.Run();
        }
        finally
        {
            TrayVersionFile.Delete();
        }

        await tokenSource.CancelAsync();
        await task;
    }

    internal static void ReBindKeys(Settings settings, KeyRegister keyRegister, Tracker tracker, Action<string>? warn = null)
    {
        foreach (var binding in BuildKeyBindings(settings, tracker))
        {
            var hotKey = binding.HotKey;
            if (keyRegister.TryAddBinding(
                    binding.Id,
                    hotKey.Shift,
                    hotKey.Control,
                    hotKey.Alt,
                    hotKey.Key,
                    binding.Action))
            {
                continue;
            }

            // Said out loud, because the alternative is a hot key that quietly does nothing. Only
            // settings.json can produce a key name the Options form cannot, so only a hand edit
            // reaches the first half of this
            warn?.Invoke(
                $"Could not bind the hot key '{hotKey.Key}'. It is either not a key name, or already registered by another application.");
        }
    }

    // Each configured hot key must map to a distinct KeyBindingIds value.
    // Reusing an id causes KeyRegister.TryAddBinding to unregister and overwrite the earlier binding.
    internal static IEnumerable<KeyBinding> BuildKeyBindings(Settings settings, Tracker tracker)
    {
        if (settings.DiscardAllHotKey is { } discardAll)
        {
            yield return new(KeyBindingIds.DiscardAll, discardAll, () => tracker.Clear());
        }

        if (settings.AcceptAllHotKey is { } acceptAll)
        {
            yield return new(KeyBindingIds.AcceptAll, acceptAll, () => tracker.AcceptAll());
        }

        if (settings.AcceptOpenHotKey is { } acceptOpen)
        {
            yield return new(KeyBindingIds.AcceptOpen, acceptOpen, () => tracker.AcceptOpen());
        }
    }

    internal record KeyBinding(int Id, HotKey HotKey, Action Action);

    static Settings? GetSettings()
    {
        try
        {
            return SettingsHelper.Read();
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Cannot start. Failed to read settings: {FilePath}", SettingsHelper.FilePath);
            IssueLauncher.LaunchForException($"Cannot start. Failed to read settings: {SettingsHelper.FilePath}", exception);
            return null;
        }
    }
}