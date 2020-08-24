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

        using var menu = new ContextMenuStrip();
        using var exit = new ToolStripMenuItem("Exit");
        exit.Click += delegate
        {
            mutex!.Dispose();
            Environment.Exit(0);
        };
        menu.Items.Add(exit);

        menu.Opening += (sender, e) =>
        {
            if (!Tracking.TrackingAny)
            {
                return;
            }
            var approveAll = new ToolStripMenuItem("Approve All");
            approveAll.Click += delegate { Tracking.ApproveAll(); };
            foreach (var delete in Tracking.Deletes)
            {
                var item = new ToolStripMenuItem($"Delete {delete.Name}");
                item.Click += delegate { Tracking.Delete(delete); };
            }
            foreach (var move in Tracking.Moves)
            {
                var item = new ToolStripMenuItem($"Accept {move.Name}");
                item.Click += delegate { Tracking.Move(move); };
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
            Icon = Resources.Icon(),
            Visible = true,
            Text = "DiffEngine",
            ContextMenuStrip = menu
        };

        Application.Run();
        await task;
    }
}