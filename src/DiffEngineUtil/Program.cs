using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Resourcer;

static class Program
{
    static void Main()
    {
        using var iconStream = Resource.AsStream("icon.ico");
        using var icon = new Icon(iconStream);
        using (new NotifyIcon
        {
            Icon = icon,
            Visible = true,
            BalloonTipText = "Hello from My Kitten",
            BalloonTipTitle = "Cat Talk",
            BalloonTipIcon = ToolTipIcon.Info,
            ContextMenuStrip = new ContextMenuStrip()
        })
        {
            new ManualResetEvent(false).WaitOne();
        }
    }
}