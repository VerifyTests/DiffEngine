using System;
using System.Drawing;
using System.Windows.Forms;

class MenuButton :
    ToolStripMenuItem
{
    public MenuButton(string text, string tooltip, Action? action= null, Image? image = null) :
        base(text, image)
    {
        ImageScaling = ToolStripItemImageScaling.None;
        ToolTipText = tooltip;
        if (action != null)
        {
            Click += delegate { action(); };
        }
    }
}