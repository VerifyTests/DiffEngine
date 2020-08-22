using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Resourcer;

static class Program
{
    static Mutex mutex;

    static void Main()
    {
        mutex = new Mutex(true, "DiffEngineUtil", out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            Environment.Exit(0);
        }

        //if()
        using var iconStream = Resource.AsStream("icon.ico");
        using var icon = new Icon(iconStream);
        var contextMenuStrip = new ContextMenuStrip();
        contextMenuStrip.Items.Add(new ToolStripButton("Exit"));
        using (var notifyIcon = new NotifyIcon
        {
            Icon = icon,
            Visible = true,
            BalloonTipText = "Hello from My Kitten",
            BalloonTipTitle = "Cat Talk",
            BalloonTipIcon = ToolTipIcon.Info,
            ContextMenuStrip = contextMenuStrip,
        })
        {
            Application.Run();
            new ManualResetEvent(false).WaitOne();

            mutex.Dispose();
        }
    }
}