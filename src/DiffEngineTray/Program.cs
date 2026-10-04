using NotifyIcon = System.Windows.Forms.NotifyIcon;

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
        using var icon = new NotifyIcon
        {
            Icon = Images.Default,
            Visible = true,
            Text = "DiffEngineTray"
        };

        void Warn(string message) =>
            icon.ShowBalloonTip(10000, "DiffEngineTray", message, ToolTipIcon.Warning);

        var settings = GetSettings();
        if (settings == null)
        {
            return;
        }

        LockedFilesHandler.AlwaysKill = settings.AlwaysKillLockingProcesses;

        // Ownership of the inline queue is decided here, once, by whether the bind succeeds, and
        // never transfers. Usually the tray wins, because it starts at login. A viewer that was
        // already running keeps the queue for as long as it lives, and this tray drives it
        // remotely for the rest of its own life rather than trying to take over mid flight.
        await using var owned = OwnedInlineHost.TryOwn(Warn);
        if (owned is null)
        {
            Log.Information("A viewer owns the inline queue. Driving it remotely.");
        }

        await using var tracker = new Tracker(
            active: () => icon.Icon = Images.Active,
            inactive: () => icon.Icon = Images.Default,
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
        using var sessionEnd = new SessionEndWindow(SessionEnding(owned, TrayVersionFile.Delete));

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

        var task = listener is null ? Task.CompletedTask : StartServer(listener, tracker, cancel);

        using var keyRegister = new KeyRegister(icon.Handle());
        ReBindKeys(settings, keyRegister, tracker, Warn);

        var menuStrip = MenuBuilder.Build(
            Application.Exit,
            async () => await OptionsFormLauncher.Launch(keyRegister, tracker),
            tracker);

        icon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
            {
                var position = Cursor.Position;
                position.Offset(-menuStrip.Width, -menuStrip.Height);
                menuStrip.Location = position;
                ShowContextMenu(icon);
            }
        };

        icon.ContextMenuStrip = menuStrip;

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

    /// <summary>
    /// What a clean exit does on its way out, for a session that ends instead: the queue staged,
    /// when it is held here, and the version marker removed.
    /// <para>
    /// The queue only where this tray owns it, since a viewer that owns it stages its own. The
    /// marker whoever owns the queue: it says a tray of this version is running, to whatever
    /// reads it to decide what it may send, and left behind by a logoff it went on saying so
    /// until a tray was next started, which after a logoff need not be this version or any.
    /// </para>
    /// <para>
    /// The queue first, being the one that cannot be had again, and the marker whether or not
    /// that worked.
    /// </para>
    /// </summary>
    /// <param name="removeMarker"><see cref="TrayVersionFile.Delete"/>, except in a test.</param>
    internal static Action SessionEnding(OwnedInlineHost? owned, Action removeMarker) =>
        () =>
        {
            try
            {
                owned?.SessionEnding();
            }
            finally
            {
                removeMarker();
            }
        };

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "ShowContextMenu")]
    static extern void ShowContextMenu(NotifyIcon icon);

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

    static Task StartServer(TcpListener listener, Tracker tracker, Cancel cancel) =>
        PiperServer.Serve(
            listener,
            payload =>
            {
                tracker.AddMove(
                    payload.Temp,
                    payload.Target,
                    payload.Exe,
                    payload.Arguments,
                    payload.CanKill,
                    payload.ProcessId,
                    Source(payload.Source));
            },
            payload => tracker.AddDelete(payload.File, Source(payload.Source)),
            cancel);

    // An empty one names nothing, as on the viewer port
    static string? Source(string? source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return null;
        }

        return source;
    }
}