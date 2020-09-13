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
        var tokenSource = new CancellationTokenSource();
        var cancellation = tokenSource.Token;
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
            Text = "DiffEngine"
        };

        var showMenu = typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)!;
        icon.MouseUp += (sender, args) =>
        {
            if (args.Button == MouseButtons.Left)
            {
                showMenu.Invoke(icon, null);
            }
        };

        await using var tracker = new Tracker(
            active: () => icon.Icon = Images.Active,
            inactive: () => icon.Icon = Images.Default);

        using var task = StartServer(tracker, cancellation);

        using var keyRegister = new KeyRegister(icon.Handle());
        var acceptHotKey = settings.AcceptAllHotKey;
        if (acceptHotKey != null)
        {
            keyRegister.TryAddBinding(
                KeyBindingIds.AcceptAll,
                acceptHotKey.Shift,
                acceptHotKey.Control,
                acceptHotKey.Alt,
                acceptHotKey.Key,
                () => tracker.AcceptAll());
        }

        icon.ContextMenuStrip = MenuBuilder.Build(
            Application.Exit,
            async () => await OptionsFormLauncher.Launch(keyRegister, tracker),
            tracker);

        Application.Run();
        tokenSource.Cancel();
        await task;
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