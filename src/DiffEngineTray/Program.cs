using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

static class Program
{

    static async Task Main()
    {
        var tokenSource = new CancellationTokenSource();
        var cancellation = tokenSource.Token;
        using var mutex = new Mutex(true, "DiffEngineUtil", out var createdNew);
        if (!createdNew)
        {
            return;
        }

        var task = PiperServer.Start(
            payload => Tracking.AddMove(payload.Temp, payload.Target, payload.IsMdi, payload.AutoRefresh, payload.ProcessId),
            payload => Tracking.AddDelete(payload.File),
            cancellation);
        var icon = Resources.Icon();
        using var menu = new ContextMenuStrip();
        using var exit = new ToolStripButton("Exit");
        exit.Click += delegate
        {
            mutex!.Dispose();
            Environment.Exit(0);
        };
        menu.Items.Add(exit);

        menu.Opening += (sender, e) =>
        {
            if (Tracking.TrackingAny)
            {
                var approveAll = new ToolStripButton("Approve All");
                approveAll.Click += delegate { Tracking.ApproveAll(); };
            }
        };
        menu.Closed += delegate
        {
            var toRemove = menu.Items.Cast<ToolStripItem>()
                .Where(x => x.Text != "Exit")
                .ToList();
            foreach (var toolStripItem in toRemove)
            {
                menu.Items.Remove(toolStripItem);
                toolStripItem.Dispose();
            }
        };

        using var notifyIcon = new NotifyIcon
        {
            Icon = icon,
            Visible = true,
            Text = "DiffEngine",
            ContextMenuStrip = menu
        };

        Application.Run();
        await task;
    }
}