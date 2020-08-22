using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Resourcer;

static class Program
{
    static void Main()
    {
        NotifyIcon icon;
        using (var iconStream = Resource.AsStream("icon.ico"))
        {
            icon = new NotifyIcon
            {
                Icon = new Icon(iconStream),
                Visible = true,
                BalloonTipText = "Hello from My Kitten",
                BalloonTipTitle = "Cat Talk",
                BalloonTipIcon = ToolTipIcon.Info
            };
        }

        icon.ContextMenuStrip = new ContextMenuStrip();
        icon.ShowBalloonTip(2000);

        //Application.Run(new Form1());
        new ManualResetEvent(false).WaitOne();
    }
}