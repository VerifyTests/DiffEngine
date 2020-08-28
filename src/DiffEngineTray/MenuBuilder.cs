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
            CleanTransientMenus(items);
            foreach (var item in BuildTrackingMenuItems(tracker))
            {
                items.Add(item);
            }
        };
        menu.Closed += delegate { CleanTransientMenus(items); };

        var submenu = new MenuButton("Options", "Expand to more options", image: Images.Options);
        submenu.DropDownItems.Add(new MenuButton("Exit", "Close the app", exit, Images.Exit));
        submenu.DropDownItems.Add(new MenuButton("Open logs", "Open logs directory", Logging.OpenDirectory, Images.Folder));
        items.Add(submenu);

        return menu;
    }

    static void CleanTransientMenus(ToolStripItemCollection items)
    {
        var toRemove = items.Cast<ToolStripItem>()
            .Where(x => x.Text != "Options");
        items.RemoveRange(toRemove);
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
            yield return new MenuButton("Pending Deletes:", "Accept all pending deletes", tracker.AcceptAllDeletes, Images.Delete);
            foreach (var delete in tracker.Deletes)
            {
                yield return new MenuButton($"{delete.Name}", $"Accept delete: {delete.File}", () => tracker.Accept(delete));
            }
        }

        if (tracker.Moves.Any())
        {
            yield return new ToolStripSeparator();
            yield return new MenuButton("Pending Moves:", "Accept all pending moves", tracker.AcceptAllMoves, Images.Accept);
            foreach (var move in tracker.Moves)
            {
                var moveMenu = new SplitButton(
                    $"{move.Name} ({move.Extension})",
                    $@"Accept move.
Source: {move.Temp}
Target: {move.Target}",
                    () => tracker.Accept(move));
                var directory = Path.GetDirectoryName(move.Temp)!;
                moveMenu.DropDownItems.Add(
                    new MenuButton(
                        "Launch diff tool",
                        $"Re-launch the diff tool: {move.Exe} {move.Arguments}",
                        () => tracker.Launch(move)));
                moveMenu.DropDownItems.Add(
                    new MenuButton(
                        "Open directory",
                        $"Open the directory: {directory}",
                        () => DirectoryLauncher.Open(directory)));
                yield return moveMenu;
            }
        }

        yield return new ToolStripSeparator();
        yield return new MenuButton("Accept all", "Accept all changes to all files", tracker.AcceptAll, Images.AcceptAll);
        yield return new MenuButton("Clear", "Clear the current racked files", tracker.Clear, Images.Clear);
    }

}