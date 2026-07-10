using NotifyIcon = System.Windows.Forms.NotifyIcon;

static class Program
{
    static async Task Main()
    {
        Logging.Init();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

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
        var settings = await GetSettings();
        if (settings == null)
        {
            return;
        }

        LockedFilesHandler.AlwaysKill = settings.AlwaysKillLockingProcesses;

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var tokenSource = new CancelSource();
        var cancel = tokenSource.Token;
        using var mutex = new Mutex(true, "DiffEngine", out var createdNew);
        if (!createdNew)
        {
            Log.Information("Mutex already exists. Exiting.");
            return;
        }

        using var icon = new NotifyIcon
        {
            Icon = Images.Default,
            Visible = true,
            Text = "DiffEngineTray"
        };

        await using var tracker = new Tracker(
            active: () => icon.Icon = Images.Active,
            inactive: () => icon.Icon = Images.Default,
            lockedFilesResolver: LockedFilesHandler.Resolve);

        using var task = StartServer(tracker, cancel);

        using var keyRegister = new KeyRegister(icon.Handle());
        ReBindKeys(settings, keyRegister, tracker);

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

        Application.Run();
        await tokenSource.CancelAsync();
        await task;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "ShowContextMenu")]
    static extern void ShowContextMenu(NotifyIcon icon);

    internal static void ReBindKeys(Settings settings, KeyRegister keyRegister, Tracker tracker)
    {
        foreach (var binding in BuildKeyBindings(settings, tracker))
        {
            var hotKey = binding.HotKey;
            keyRegister.TryAddBinding(
                binding.Id,
                hotKey.Shift,
                hotKey.Control,
                hotKey.Alt,
                hotKey.Key,
                binding.Action);
        }
    }

    // Each configured hot key must map to a distinct KeyBindingIds value.
    // Reusing an id causes KeyRegister.TryAddBinding to unregister and overwrite the earlier binding.
    internal static IEnumerable<KeyBinding> BuildKeyBindings(Settings settings, Tracker tracker)
    {
        if (settings.DiscardAllHotKey is { } discardAll)
        {
            yield return new(KeyBindingIds.DiscardAll, discardAll, tracker.Clear);
        }

        if (settings.AcceptAllHotKey is { } acceptAll)
        {
            yield return new(KeyBindingIds.AcceptAll, acceptAll, tracker.AcceptAll);
        }

        if (settings.AcceptOpenHotKey is { } acceptOpen)
        {
            yield return new(KeyBindingIds.AcceptOpen, acceptOpen, tracker.AcceptOpen);
        }
    }

    internal record KeyBinding(int Id, HotKey HotKey, Action Action);

    static async Task<Settings?> GetSettings()
    {
        try
        {
            return await SettingsHelper.Read();
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Cannot start. Failed to read settings: {FilePath}", SettingsHelper.FilePath);
            IssueLauncher.LaunchForException($"Cannot start. Failed to read settings: {SettingsHelper.FilePath}", exception);
            return null;
        }
    }

    static Task StartServer(Tracker tracker, Cancel cancel) =>
        PiperServer.Start(
            payload =>
            {
                tracker.AddMove(
                    payload.Temp,
                    payload.Target,
                    payload.Exe,
                    payload.Arguments,
                    payload.CanKill,
                    payload.ProcessId);
            },
            payload => tracker.AddDelete(payload.File),
            cancel);
}