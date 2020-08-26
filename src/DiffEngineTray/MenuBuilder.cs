using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

static class MenuBuilder
{
    public static ContextMenuStrip Build(Action exit, Tracker tracker)
    {
        var menu = new ContextMenuStrip();
        var exitItem = new MenuButton("Exit", Images.Exit, exit);
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
        yield return new MenuButton("Accept All", Images.AcceptAll, tracker.AcceptAll);
        yield return new MenuButton("Clear", Images.Clear, tracker.Clear);
        yield return new ToolStripSeparator();

        foreach (var delete in tracker.Deletes)
        {
            yield return new MenuButton($"Delete: {delete.Name}", Images.Delete, () => tracker.Accept(delete));
        }

        foreach (var move in tracker.Moves)
        {
            yield return new MenuButton($"Accept: {move.Name} ({move.Extension})", Images.Accept, () => tracker.Accept(move));
        }
    }
}