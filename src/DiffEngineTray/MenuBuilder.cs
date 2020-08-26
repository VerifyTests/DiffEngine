using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

static class MenuBuilder
{
    public static ContextMenuStrip Build(Action exit, Tracker tracker)
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
            foreach (var item in BuildTrackingMenuItems(tracker))
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

    static IEnumerable<ToolStripMenuItem> BuildTrackingMenuItems(Tracker tracker)
    {
        if (!tracker.TrackingAny)
        {
            yield break;
        }

        var acceptAll = new ToolStripMenuItem("Accept All")
        {
            Image = Resources.AcceptAllIcon
        };

        acceptAll.Click += delegate { tracker.AcceptAll(); };
        yield return acceptAll;
        foreach (var delete in tracker.Deletes)
        {
            var item = new ToolStripMenuItem($"Delete {delete.Name}")
            {
                Image = Resources.DeleteIcon
            };
            item.Click += delegate { tracker.Accept(delete); };
            yield return item;
        }

        foreach (var move in tracker.Moves)
        {
            var item = new ToolStripMenuItem($"Accept {move.Name} ({move.Extension})")
            {
                Image = Resources.AcceptIcon
            };
            item.Click += delegate { tracker.Accept(move); };
            yield return item;
        }
    }
}