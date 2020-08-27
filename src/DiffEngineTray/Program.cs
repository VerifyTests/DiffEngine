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

        var tokenSource = new CancellationTokenSource();
        var cancellation = tokenSource.Token;
        using var mutex = new Mutex(true, "DiffEngine", out var createdNew);
        if (!createdNew)
        {
            Log.Logger.Information("Mutex already exists. Exiting.");
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

        using var task = PiperServer.Start(
            payload => tracker.AddMove(payload.Temp, payload.Target, payload.CanKill, payload.ProcessId, payload.ProcessStartTime),
            payload => tracker.AddDelete(payload.File),
            cancellation);
        var menu = MenuBuilder.Build(
            () =>
            {
                tokenSource.Cancel();
                mutex!.Dispose();
                Environment.Exit(0);
            },
            tracker);

        notifyIcon.ContextMenuStrip = menu;

        Application.Run();
        await task;
    }
}