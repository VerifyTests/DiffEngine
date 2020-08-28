using System;
using System.Drawing;
using System.Windows.Forms;

class SplitButton :
    ToolStripSplitButton
{
    public SplitButton(string text, string tooltip, Action? action= null, Image? image = null) :
        base(text, image)
    {
        ImageScaling = ToolStripItemImageScaling.None;
        ToolTipText = tooltip;
        if (action != null)
        {
            ButtonClick += delegate { action(); };
        }
    }
}