using System;
using System.Collections.Generic;
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
        using var mutex = new Mutex(true, "DiffEngine", out var createdNew);
        if (!createdNew)
        {
            return;
        }

        var task = PiperServer.Start(
            payload => Tracking.AddMove(payload.Temp, payload.Target, payload.CanKill, payload.ProcessId),
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

            foreach (var item in BuildMenuItems())
            {
                menu.Items.Insert(0, item);
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

    static IEnumerable<ToolStripMenuItem> BuildMenuItems()
    {
        var acceptAll = new ToolStripMenuItem("Accept All");
        acceptAll.Click += delegate { Tracking.AcceptAll(); };
        yield return acceptAll;
        foreach (var delete in Tracking.Deletes)
        {
            var item = new ToolStripMenuItem($"Delete {delete.Name}");
            item.Click += delegate { Tracking.Delete(delete); };
            yield return item;
        }

        foreach (var move in Tracking.Moves)
        {
            var item = new ToolStripMenuItem($"Accept {move.Name} ({move.Extension})");
            item.Click += delegate { Tracking.Move(move); };
            yield return item;
        }
    }
}