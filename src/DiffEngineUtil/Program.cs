using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Resourcer;

static class Program
{
    static Mutex mutex;

    static async Task Main(string[] args)
    {
        mutex = new Mutex(true, "DiffEngineUtil", out var createdNew);
        if (!createdNew)
        {
            await Piper.Send(args);
            Environment.Exit(0);
        }

        var resetEvent = new ManualResetEvent(false);
        var icon = BuildIcon();
        using var menu = new ContextMenuStrip();
        using var exit = new ToolStripButton("Exit");
        exit.Click += (o, args) =>
        {
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
        resetEvent.WaitOne();

        mutex.Dispose();
    }

    static Icon BuildIcon()
    {
        using var iconStream = Resource.AsStream("icon.ico");
        return new Icon(iconStream);
    }
}