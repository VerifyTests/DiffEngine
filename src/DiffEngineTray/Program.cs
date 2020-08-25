using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

static class Program
{
    static async Task Main()
    {
        var tokenSource = new CancellationTokenSource();
        var cancellation = tokenSource.Token;
        using var mutex = new Mutex(true, "DiffEngine", out var createdNew);
        if (!createdNew)
        {
            return;
        }

        using var notifyIcon = new NotifyIcon
        {
            Icon = Resources.DefaultIcon,
            Visible = true,
            Text = "DiffEngine"
        };

        await using var tracking = new Tracking(
            active: () => notifyIcon.Icon=Resources.ActiveIcon,
            inactive: () => notifyIcon.Icon=Resources.DefaultIcon);

        var task = PiperServer.Start(
            payload => tracking.AddMove(payload.Temp, payload.Target, payload.CanKill, payload.ProcessId),
            payload => tracking.AddDelete(payload.File),
            cancellation);

        var menu = MenuBuilder.Build(
            () =>
            {
                mutex!.Dispose();
                Environment.Exit(0);
            },
            tracking);

        notifyIcon.ContextMenuStrip = menu;

        Application.Run();
        await task;
    }
}