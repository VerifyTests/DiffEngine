using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Serilog;

static class Program
{
    static async Task Main()
    {
        Logging.Init();

        var settings = await SettingsHelper.Read();
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

        using var notifyIcon = new NotifyIcon
        {
            Icon = Images.Default,
            Visible = true,
            Text = "DiffEngine"
        };

        await using var tracker = new Tracker(
            active: () => notifyIcon.Icon = Images.Active,
            inactive: () => notifyIcon.Icon = Images.Default);

        using var task = StartServer(tracker, cancellation);

        using var keyRegister = new KeyRegister(notifyIcon.Handle());
        var acceptAllHotKey = settings.AcceptAllHotKey;
        if (acceptAllHotKey != null)
        {
            keyRegister.TryAddBinding(
                KeyBindingIds.AcceptAll,
                acceptAllHotKey.Shift,
                acceptAllHotKey.Control,
                acceptAllHotKey.Alt,
                acceptAllHotKey.Key,
                () => tracker.AcceptAll());
        }

        notifyIcon.ContextMenuStrip = MenuBuilder.Build(
            Application.Exit,
            async () => await OptionsFormLauncher.Launch(keyRegister,tracker),
            tracker);

        Application.Run();
        tokenSource.Cancel();
        await task;
    }

    static Task StartServer(Tracker tracker, CancellationToken cancellation)
    {
        return PiperServer.Start(
            payload =>
            {
                (int, DateTime)? process = null;
                if (payload.ProcessId != null &&
                    payload.ProcessStartTime != null)
                {
                    process = (payload.ProcessId.Value, payload.ProcessStartTime.Value);
                }

                tracker.AddMove(
                    payload.Temp,
                    payload.Target,
                    payload.Exe,
                    payload.Arguments,
                    payload.CanKill,
                    process);
            },
            payload => tracker.AddDelete(payload.File),
            cancellation);
    }
}