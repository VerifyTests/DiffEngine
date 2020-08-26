using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

static class MenuBuilder
{
    public static ContextMenuStrip Build(Action exit, Tracker tracker)
    {
        var menu = new ContextMenuStrip();
        var exitItem = new ActionMenuItem("Exit", Images.ExitIcon, exit);
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

    static IEnumerable<ToolStripItem> BuildTrackingMenuItems(Tracker tracker)
    {
        if (!tracker.TrackingAny)
        {
            yield break;
        }

        yield return new ToolStripSeparator();
        yield return new ActionMenuItem("Accept All", Images.AcceptAllIcon, tracker.AcceptAll);
        yield return new ActionMenuItem("Clear", Images.ClearIcon, tracker.Clear);
        yield return new ToolStripSeparator();

        foreach (var delete in tracker.Deletes)
        {
            yield return new ActionMenuItem($"Delete: {delete.Name}", Images.DeleteIcon, () => tracker.Accept(delete));
        }

        foreach (var move in tracker.Moves)
        {
            yield return new ActionMenuItem($"Accept: {move.Name} ({move.Extension})", Images.AcceptIcon, () => tracker.Accept(move));
        }
    }
}