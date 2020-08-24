using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Resourcer;

static class Program
{
    static ConcurrentBag<TrackedPair> tracked = new ConcurrentBag<TrackedPair>();

    static async Task Main()
    {
        var tokenSource = new CancellationTokenSource();
        var cancellation = tokenSource.Token;
        using var mutex = new Mutex(true, "DiffEngineUtil", out var createdNew);
        if (!createdNew)
        {
            return;
        }

        var task = PiperServer.Start(strings => tracked.Add(new TrackedPair()), cancellation);
        var icon = BuildIcon();
        using var menu = new ContextMenuStrip();
        using var exit = new ToolStripButton("Exit");
        exit.Click += delegate
        {
            mutex!.Dispose();
            Environment.Exit(0);
        };
        menu.Items.Add(exit);

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

    static Icon BuildIcon()
    {
        using var iconStream = Resource.AsStream("icon.ico");
        return new Icon(iconStream);
    }
}

class TrackedPair
{
}