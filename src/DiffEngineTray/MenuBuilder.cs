using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

static class MenuBuilder
{
    static List<ToolStripItem>? itemsToCleanup;

    public static ContextMenuStrip Build(Action exit, Action launchOptions, Tracker tracker)
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
        menu.Font = new Font(menu.Font.FontFamily, 10);
        menu.Closed += delegate { CleanTransientMenus(items); };
        items.Add(new MenuButton("Exit", exit, Images.Exit));
        items.Add(new MenuButton("Options", launchOptions, Images.Options));
        items.Add(new MenuButton("Open logs", Logging.OpenDirectory, Images.Folder));
        items.Add(new MenuButton("Raise issue", IssueLauncher.Launch, Images.Link));
        return menu;
    }

    static List<ToolStripItem> NonDefaultMenus(ToolStripItemCollection items)
    {
        return items.Cast<ToolStripItem>()
            .Where(x => x.Text != "Exit" &&
                        x.Text != "Options" &&
                        x.Text != "Open logs" &&
                        x.Text != "Raise issue")
            .ToList();
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

    static IEnumerable<ToolStripItem> BuildTrackingMenuItems(Tracker tracker)
    {
        if (!tracker.TrackingAny)
        {
            yield break;
        }

        var deletes = tracker.Deletes
            .OrderBy(x => x.File)
            .ToList();

        var moves = tracker.Moves
            .OrderBy(x => x.Temp)
            .ToList();
        var count = moves.Count + deletes.Count;

        yield return new ToolStripSeparator();

        yield return new MenuButton($"Clear ({count})", tracker.Clear, Images.Clear);

        if (deletes.Any())
        {
            yield return new ToolStripSeparator();
            yield return new MenuButton($"Pending Deletes ({deletes.Count}):", tracker.AcceptAllDeletes, Images.Delete);
            foreach (var delete in deletes)
            {
                var menu = new SplitButton($"{delete.Name}", () => tracker.Accept(delete));
                menu.AddRange(
                    new MenuButton("Accept change", () => tracker.Accept(delete)),
                    new MenuButton("Open directory", () => ExplorerLauncher.ShowFileInExplorer(delete.File)));
                yield return menu;
            }
        }

        if (moves.Any())
        {
            yield return new ToolStripSeparator();
            yield return new MenuButton($"Pending Moves ({moves.Count}):", tracker.AcceptAllMoves, Images.Accept);
            foreach (var move in moves)
            {
                var menu = new SplitButton($"{move.Name} ({move.Extension})", () => tracker.Accept(move));
                menu.AddRange(
                    new MenuButton("Accept change", () => tracker.Accept(move)),
                    new MenuButton("Launch diff tool", () => ProcessLauncher.Launch(move)),
                    new MenuButton("Open directory", () => ExplorerLauncher.ShowFileInExplorer(move.Temp)));
                yield return menu;
            }
        }

        yield return new ToolStripSeparator();
        yield return new MenuButton($"Accept all ({count})", tracker.AcceptAll, Images.AcceptAll);
    }
}