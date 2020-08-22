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

        var resetEvent = new ManualResetEvent(false);
        using var iconStream = Resource.AsStream("icon.ico");
        using var icon = new Icon(iconStream);
        using var menu = new ContextMenuStrip();
        using var exit = new ToolStripButton("Exit");
        exit.Click += (o, args) =>
        {
            resetEvent.Set();
            Application.Exit();
        };
        menu.Items.Add(exit);

        using var notifyIcon = new NotifyIcon
        {
            Icon = icon,
            Visible = true,
            Text = "DiffEngine",
            ContextMenuStrip = menu,
        };

        Application.Run();
        resetEvent.WaitOne();

        mutex.Dispose();
    }
}