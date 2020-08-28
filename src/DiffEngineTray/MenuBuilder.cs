using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

static class MenuBuilder
{
    public static ContextMenuStrip Build(Action exit, Tracker tracker)
    {
        var menu = new ContextMenuStrip();
        var items = menu.Items;

        menu.Opening += delegate
        {
            foreach (var item in BuildTrackingMenuItems(tracker))
            {
                items.Add(item);
            }
        };
        menu.Closed += delegate { CleanTransientMenus(items); };

        var submenu = new MenuButton("Options", image: Images.Options);
        submenu.DropDownItems.Add(new MenuButton("Exit", exit, Images.Exit));
        submenu.DropDownItems.Add(new MenuButton("Open logs", Logging.OpenDirectory, Images.Folder));
        items.Add(submenu);

        return menu;
    }

    static void CleanTransientMenus(ToolStripItemCollection items)
    {
        var toRemove = NonDefaultMenus(items);
        items.RemoveRange(toRemove);
    }

    static List<ToolStripItem> NonDefaultMenus(ToolStripItemCollection items)
    {
        return items.Cast<ToolStripItem>()
            .Where(x => x.Text != "Options")
            .ToList();
    }

    static IEnumerable<ToolStripItem> BuildTrackingMenuItems(Tracker tracker)
    {
        if (!tracker.TrackingAny)
        {
            yield break;
        }

        if (tracker.Deletes.Any())
        {
            yield return new ToolStripSeparator();
            yield return new MenuButton("Pending Deletes:", tracker.AcceptAllDeletes, Images.Delete);
            foreach (var delete in tracker.Deletes)
            {
                yield return new MenuButton($"{delete.Name}", () => tracker.Accept(delete));
            }
        }

        if (tracker.Moves.Any())
        {
            yield return new ToolStripSeparator();
            yield return new MenuButton("Pending Moves:", tracker.AcceptAllMoves, Images.Accept);
            foreach (var move in tracker.Moves)
            {
                var moveMenu = new SplitButton(
                    $"{move.Name} ({move.Extension})",
                    () => tracker.Accept(move));
                var directory = Path.GetDirectoryName(move.Temp)!;
                moveMenu.DropDownItems.Add(
                    new MenuButton(
                        "Launch diff tool",
                        () => Tracker.Launch(move)));
                moveMenu.DropDownItems.Add(
                    new MenuButton(
                        "Open directory",
                        () => DirectoryLauncher.Open(directory)));
                yield return moveMenu;
            }
        }

        yield return new ToolStripSeparator();
        yield return new MenuButton("Accept all", tracker.AcceptAll, Images.AcceptAll);
        yield return new MenuButton("Clear", tracker.Clear, Images.Clear);
    }

}