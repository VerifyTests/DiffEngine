using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

static class ToolTopExtensions
{
    public static void RemoveRange(this ContextMenuStrip menu, IEnumerable<ToolStripItem> items)
    {
        foreach (var toolStripItem in items.ToList())
        {
            menu.Items.Remove(toolStripItem);
            toolStripItem.Dispose();
        }
    }
}