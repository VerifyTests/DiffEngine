using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

static class MenuBuilder
{
    public static void Build(ContextMenuStrip menu, Action exit, Action launchOptions, Tracker tracker)
    {
        var items = menu.Items;
        menu.Opening += delegate
        {
            DisposePreviousItems(items);

            foreach (var item in BuildTrackingMenuItems(tracker))
            {
                items.Add(item);
            }
        };
        menu.Font = new Font(menu.Font.FontFamily, 10);
        items.Add(new MenuButton("Exit", exit, Images.Exit));
        items.Add(new MenuButton("Options", launchOptions, Images.Options));
        items.Add(new MenuButton("Open logs", Logging.OpenDirectory, Images.Folder));
        items.Add(new MenuButton("Raise issue", IssueLauncher.Launch, Images.Link));
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

    static void DisposePreviousItems(ToolStripItemCollection items)
    {
        foreach (var item in NonDefaultMenus(items))
        {
            items.Remove(item);
            item.Dispose();
        }
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

        var groups = deletes.Select(x => x.Group)
            .Concat(moves.Select(x => x.Group))
            .Distinct()
            .ToList();

        foreach (var group in groups)
        {
            foreach (var toolStripItem in BuildMovesAndDeletes(
                group,
                tracker,
                deletes.Where(x => x.Group == group).ToList(),
                moves.Where(x => x.Group == group).ToList()))
            {
                yield return toolStripItem;
            }
        }

        yield return new MenuButton($"Clear ({count})", tracker.Clear, Images.Clear);
        yield return new MenuButton($"Accept all ({count})", tracker.AcceptAll, Images.AcceptAll);
    }

    static IEnumerable<ToolStripItem> BuildMovesAndDeletes(string? name, Tracker tracker, List<TrackedDelete> deletes, List<TrackedMove> moves)
    {
        if (name != null)
        {
            yield return new MenuButton(name, null, Images.VisualStudio);
        }

        if (deletes.Any())
        {
            yield return new MenuButton($"Pending Deletes ({deletes.Count}):", () => tracker.Accept(deletes), Images.Delete);
            foreach (var delete in deletes)
            {
                yield return BuildDelete(delete, () => tracker.Accept(delete));
            }
        }

        if (moves.Any())
        {
            yield return new MenuButton($"Pending Moves ({moves.Count}):", () => tracker.Accept(moves), Images.Accept);
            foreach (var move in moves)
            {
                yield return BuildMove(move, () => tracker.Accept(move));
            }
        }

        yield return new ToolStripSeparator();
    }

    static ToolStripItem BuildDelete(TrackedDelete delete, Action accept)
    {
        var menu = new SplitButton($"{delete.Name}", accept);
        menu.AddRange(
            new MenuButton("Accept delete", accept),
            BuildShowInExplorer(delete.File));
        return menu;
    }

    static ToolStripItem BuildMove(TrackedMove move, Action accept)
    {
        var menu = new SplitButton($"{move.Name} ({move.Extension})", accept);
        menu.AddRange(
            new MenuButton("Accept move", accept),
            new MenuButton("Open diff tool", () => DiffToolLauncher.Launch(move)),
            BuildShowInExplorer(move.Temp));
        return menu;
    }

    static MenuButton BuildShowInExplorer(string file)
    {
        return new MenuButton("Open directory", () => ExplorerLauncher.ShowFileInExplorer(file));
    }
}