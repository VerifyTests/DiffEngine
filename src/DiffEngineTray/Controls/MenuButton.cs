using System.Drawing;
using System.Windows.Forms;

class MenuButton :
    ToolStripMenuItem
{
    public MenuButton(string text, Action? action = null, Image? image = null) :
        base(text, image)
    {
        if (action != null)
        {
            Click += delegate { action(); };
            CanSelect = true;
        }
    }

    public override bool CanSelect { get; }
}