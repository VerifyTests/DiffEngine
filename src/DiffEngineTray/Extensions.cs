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
        }
    }

    public static void AddRange(this ToolStripMenuItem target, params ToolStripItem[] items)
    {
        target.DropDownItems.AddRange(items);
    }

    public static void AddRange(this ToolStripSplitButton target, params ToolStripItem[] items)
    {
        target.DropDownItems.AddRange(items);
    }
}