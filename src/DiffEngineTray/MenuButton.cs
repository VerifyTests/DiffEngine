using System;
using System.Drawing;
using System.Windows.Forms;

class MenuButton :
    ToolStripMenuItem
{
    public MenuButton(string text, Action action, string tooltip, Image? image = null) :
        base(text, image)
    {
        ImageScaling = ToolStripItemImageScaling.None;
        ToolTipText = tooltip;
        Click += delegate { action(); };
    }
}