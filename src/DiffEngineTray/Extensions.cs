using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

static class Extensions
{
    public static void RemoveRange(this ToolStripItemCollection target, IEnumerable<ToolStripItem> items)
    {
        foreach (var toolStripItem in items.ToList())
        {
            target.Remove(toolStripItem);
            toolStripItem.Dispose();
        }
    }
}