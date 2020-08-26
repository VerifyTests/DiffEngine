using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

static class MenuBuilder
{
    public static ContextMenuStrip Build(Action exit, Tracking tracking)
    {
        var menu = new ContextMenuStrip();
        var exitItem = new ToolStripMenuItem("Exit")
        {
            Image = Resources.ExitIcon
        };
        exitItem.Click += delegate
        {
            exit();
        };
        menu.Items.Add(exitItem);

        menu.Opening += delegate
        {
            foreach (var item in BuildTrackingMenuItems(tracking))
            {
                menu.Items.Insert(0, item);
            }
        };
        menu.Closed += delegate
        {
            var toRemove = menu.Items.Cast<ToolStripItem>()
                .Where(x => x.Text != "Exit");
            menu.RemoveRange(toRemove);
        };
        return menu;
    }

    static IEnumerable<ToolStripMenuItem> BuildTrackingMenuItems(Tracking tracking)
    {
        if (!tracking.TrackingAny)
        {
            yield break;
        }

        var acceptAll = new ToolStripMenuItem("Accept All")
        {
            Image = Resources.AcceptAllIcon
        };

        acceptAll.Click += delegate { tracking.AcceptAll(); };
        yield return acceptAll;
        foreach (var delete in tracking.Deletes)
        {
            var item = new ToolStripMenuItem($"Delete {delete.Name}")
            {
                Image = Resources.DeleteIcon
            };
            item.Click += delegate { tracking.Delete(delete); };
            yield return item;
        }

        foreach (var move in tracking.Moves)
        {
            var item = new ToolStripMenuItem($"Accept {move.Name} ({move.Extension})")
            {
                Image = Resources.AcceptIcon
            };
            item.Click += delegate { tracking.Move(move); };
            yield return item;
        }
    }
}