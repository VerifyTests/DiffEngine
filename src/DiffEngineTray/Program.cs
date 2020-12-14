using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Serilog;

static class Program
{
    static async Task Main()
    {
        Logging.Init();

        var settings = await GetSettings();
        if (settings == null)
        {
            return;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        CancellationTokenSource tokenSource = new();
        var cancellation = tokenSource.Token;
        using Mutex mutex = new(true, "DiffEngine", out var createdNew);
        if (!createdNew)
        {
            Log.Information("Mutex already exists. Exiting.");
            return;
        }

        using NotifyIcon icon = new()
        {
            Icon = Images.Default,
            Visible = true,
            Text = "DiffEngine"
        };

        var showMenu = typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)!;
        icon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
            {
                showMenu.Invoke(icon, null);
            }
        };

        await using Tracker tracker = new(
            active: () => icon.Icon = Images.Active,
            inactive: () => icon.Icon = Images.Default);

        using var task = StartServer(tracker, cancellation);

        using KeyRegister keyRegister = new(icon.Handle());
        ReBindKeys(settings, keyRegister, tracker);

        var menuStrip = MenuBuilder.Build(
            Application.Exit,
            async () => await OptionsFormLauncher.Launch(keyRegister, tracker),
            tracker);
        menuStrip.Opening += delegate { menuStrip.Location = Cursor.Position; };
        icon.ContextMenuStrip = menuStrip;

        Application.Run();
        tokenSource.Cancel();
        await task;
    }

    static void ReBindKeys(Settings settings, KeyRegister keyRegister, Tracker tracker)
    {
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

    static Task StartServer(Tracker tracker, CancellationToken cancellation)
    {
        return PiperServer.Start(
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
            cancellation);
    }
}