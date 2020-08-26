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
        var items = menu.Items;
        items.Add(exitItem);

        menu.Opening += delegate
        {
            CleanTransientMenus(items);
            foreach (var item in BuildTrackingMenuItems(tracker))
            {
                items.Insert(0, item);
            }
        };
        menu.Closed += delegate { CleanTransientMenus(items); };
        return menu;
    }

    static void CleanTransientMenus(ToolStripItemCollection items)
    {
        var toRemove = items.Cast<ToolStripItem>()
            .Where(x => x.Text != "Exit");
        items.RemoveRange(toRemove);
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

        var clear = new ToolStripMenuItem("Clear");

        clear.Click += delegate { tracker.Clear(); };
        yield return clear;
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