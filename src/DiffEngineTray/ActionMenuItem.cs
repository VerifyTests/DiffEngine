using System;
using System.Drawing;
using System.Windows.Forms;

class ActionMenuItem :
    ToolStripMenuItem
{
    public ActionMenuItem(string text, Image image, Action action) :
        base(text, image)
    {
        ImageScaling = ToolStripItemImageScaling.None;
        Click += delegate { action(); };
    }
}