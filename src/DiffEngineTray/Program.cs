using NotifyIcon = System.Windows.Forms.NotifyIcon;

static class Program
{
    static async Task Main()
    {
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
        var settings = await GetSettings();
        if (settings == null)
        {
            return;
        }

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
            inactive: () => icon.Icon = Images.Default);

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
        tokenSource.Cancel();
        await task;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "ShowContextMenu")]
    static extern void ShowContextMenu(NotifyIcon icon);

    static void ReBindKeys(Settings settings, KeyRegister keyRegister, Tracker tracker)
    {
        var discardAllHotKey = settings.DiscardAllHotKey;
        if (discardAllHotKey != null)
        {
            keyRegister.TryAddBinding(
                KeyBindingIds.DiscardAll,
                discardAllHotKey.Shift,
                discardAllHotKey.Control,
                discardAllHotKey.Alt,
                discardAllHotKey.Key,
                tracker.Clear);
        }

        var acceptAllHotKey = settings.AcceptAllHotKey;
        if (acceptAllHotKey != null)
        {
            keyRegister.TryAddBinding(
                KeyBindingIds.AcceptAll,
                acceptAllHotKey.Shift,
                acceptAllHotKey.Control,
                acceptAllHotKey.Alt,
                acceptAllHotKey.Key,
                tracker.AcceptAll);
        }

        var acceptOpenHotKey = settings.AcceptOpenHotKey;
        if (acceptOpenHotKey != null)
        {
            keyRegister.TryAddBinding(
                KeyBindingIds.AcceptAll,
                acceptOpenHotKey.Shift,
                acceptOpenHotKey.Control,
                acceptOpenHotKey.Alt,
                acceptOpenHotKey.Key,
                tracker.AcceptOpen);
        }
    }

    static async Task<Settings?> GetSettings()
    {
        try
        {
            return await SettingsHelper.Read();
        }
        catch (Exception exception)
        {
            var message = $"Cannot start. Failed to read settings: {SettingsHelper.FilePath}";
            Log.Fatal(exception, message);
            IssueLauncher.LaunchForException(message, exception);
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