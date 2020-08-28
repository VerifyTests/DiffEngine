using System;
using System.Drawing;
using System.Windows.Forms;

class MenuButton :
    ToolStripMenuItem
{
    public MenuButton(string text, Action? action= null, Image? image = null) :
        base(text, image)
    {
        ImageScaling = ToolStripItemImageScaling.None;
        if (action != null)
        {
            Click += delegate { action(); };
        }
    }
}