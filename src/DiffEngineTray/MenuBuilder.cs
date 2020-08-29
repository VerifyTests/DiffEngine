using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

static class MenuBuilder
{
    static List<ToolStripItem>? itemsToCleanup;

    public static ContextMenuStrip Build(Action exit, Tracker tracker)
    {
        var menu = new ContextMenuStrip();
        var items = menu.Items;

        menu.Opening += delegate
        {
            DisposePreviousItems();

            foreach (var item in BuildTrackingMenuItems(tracker))
            {
                items.Add(item);
            }
        };
        menu.Closed += delegate { CleanTransientMenus(items); };
        items.Add(BuildOptions(exit));
        return menu;
    }

    static MenuButton BuildOptions(Action exit)
    {
        var menu = new MenuButton("Options", image: Images.Options);
        menu.AddRange(
            new MenuButton("Exit", exit, Images.Exit),
            new MenuButton("Open logs", Logging.OpenDirectory, Images.Folder),
            new MenuButton("Raise issue", IssueLauncher.Launch, Images.Link));
        return menu;
    }

    static void DisposePreviousItems()
    {
        if (itemsToCleanup == null)
        {
            return;
        }
        foreach (var item in itemsToCleanup)
        {
            item.Dispose();
        }
    }

    static void CleanTransientMenus(ToolStripItemCollection items)
    {
        itemsToCleanup = NonDefaultMenus(items);
        items.RemoveRange(itemsToCleanup);
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

        yield return new MenuButton("Clear", tracker.Clear, Images.Clear);
        if (tracker.Deletes.Any())
        {
            yield return new ToolStripSeparator();
            yield return new MenuButton("Pending Deletes:", tracker.AcceptAllDeletes, Images.Delete);
            foreach (var delete in tracker.Deletes)
            {
                var menu = new SplitButton($"{delete.Name}", () => tracker.Accept(delete));
                menu.AddRange(
                    new MenuButton("Accept change", () => tracker.Accept(delete)),
                    new MenuButton("Open directory", () => ExplorerLauncher.OpenFile(delete.File)));
                yield return menu;
            }
        }

        if (tracker.Moves.Any())
        {
            yield return new ToolStripSeparator();
            yield return new MenuButton("Pending Moves:", tracker.AcceptAllMoves, Images.Accept);
            foreach (var move in tracker.Moves)
            {
                var menu = new SplitButton($"{move.Name} ({move.Extension})", () => tracker.Accept(move));
                menu.AddRange(
                    new MenuButton("Accept change", () => tracker.Accept(move)),
                    new MenuButton("Launch diff tool", () => ProcessLauncher.Launch(move)),
                    new MenuButton("Open directory", () => ExplorerLauncher.OpenFile(move.Temp)));
                yield return menu;
            }
        }

        yield return new ToolStripSeparator();
        yield return new MenuButton("Accept all", tracker.AcceptAll, Images.AcceptAll);
    }
}