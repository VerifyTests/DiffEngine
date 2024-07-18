static class MenuBuilder
{
    public static ContextMenuStrip Build(Action exit, Action launchOptions, Tracker tracker)
    {
        var menu = new ContextMenuStrip
        {
            DefaultDropDownDirection = ToolStripDropDownDirection.AboveLeft
        };
        var items = menu.Items;
        menu.Closed += delegate
        {
            RemovePreviousItems(items);
        };
        menu.Opening += delegate
        {
            DisposePreviousItems(items);
            foreach (var item in BuildTrackingMenuItems(tracker))
            {
                items.Add(item);
            }
        };
        menu.Font = new(menu.Font.FontFamily, 10);
        items.Add(new MenuButton("Exit", exit, Images.Exit));
        items.Add(new MenuButton("Options", launchOptions, Images.Options));
        items.Add(new MenuButton("Open logs", Logging.OpenDirectory, Images.Folder));
        items.Add(new MenuButton("Purge verified files", FilePurger.Launch, Images.Folder));
        items.Add(new MenuButton("Raise issue", IssueLauncher.Launch, Images.Link));
        return menu;
    }

    static IEnumerable<ToolStripItem> NonDefaultMenus(ToolStripItemCollection items) =>
        items
            .Cast<ToolStripItem>()
            .Where(_ => _.Text != "Exit" &&
                        _.Text != "Options" &&
                        _.Text != "Open logs" &&
                        _.Text != "Raise issue" &&
                        _.Text != "Purge verified files")
            .ToList();

    static void RemovePreviousItems(ToolStripItemCollection items)
    {
        // Use ToList to avoid deferred execution of NonDefaultMenus
        foreach (var item in NonDefaultMenus(items))
        {
            items.Remove(item);
        }
    }

    static void DisposePreviousItems(ToolStripItemCollection items)
    {
        // Use ToList to avoid deferred execution of NonDefaultMenus
        foreach (var item in NonDefaultMenus(items))
        {
            item.Dispose();
        }
    }

    static IEnumerable<ToolStripItem> BuildTrackingMenuItems(Tracker tracker)
    {
        if (!tracker.TrackingAny)
        {
            yield break;
        }

        var deletes = tracker
            .Deletes
            .OrderBy(_ => _.File)
            .ToList();

        var moves = tracker
            .Moves
            .OrderBy(_ => _.Temp)
            .ToList();

        var count = moves.Count + deletes.Count;

        yield return new ToolStripSeparator();

        foreach (var item in BuildGroupedMenuItems(tracker, deletes, moves))
        {
            yield return item;
        }

        yield return new MenuButton($"Discard ({count})", tracker.Clear, Images.Discard);
        yield return new MenuButton($"Accept all ({count})", tracker.AcceptAll, Images.AcceptAll);
    }

    static IEnumerable<ToolStripItem> BuildGroupedMenuItems(Tracker tracker, List<TrackedDelete> deletes, List<TrackedMove> moves)
    {
        var groups = deletes
            .Select(_ => _.Group)
            .Concat(moves.Select(_ => _.Group))
            .Distinct()
            .ToList();

        var addedCount = 0;
        foreach (var group in groups)
        {
            foreach (var toolStripItem in BuildMovesAndDeletes(
                         group,
                         tracker,
                         deletes
                             .Where(_ => _.Group == group)
                             .ToList(),
                         moves
                             .Where(_ => _.Group == group)
                             .ToList()))
            {
                yield return toolStripItem;
                addedCount++;
                if (addedCount == 20)
                {
                    yield return new MenuButton("Only 20 items rendered");
                    yield break;
                }
            }
        }
    }

    static IEnumerable<ToolStripItem> BuildMovesAndDeletes(string? name, Tracker tracker, List<TrackedDelete> deletes, List<TrackedMove> moves)
    {
        if (name != null)
        {
            yield return new MenuButton(name, null, Images.VisualStudio);
        }

        if (deletes.Count != 0)
        {
            yield return new MenuButton($"Pending Deletes ({deletes.Count}):", () => tracker.Accept(deletes), Images.Delete);
            foreach (var delete in deletes)
            {
                yield return BuildDelete(delete, () => tracker.Accept(delete));
            }
        }

        if (moves.Count != 0)
        {
            yield return new MenuButton($"Pending Moves ({moves.Count}):", () => tracker.Accept(moves), Images.Accept);
            foreach (var move in moves)
            {
                yield return BuildMove(
                    move,
                    () => tracker.Accept(move),
                    () => tracker.Discard(move));
            }
        }

        yield return new ToolStripSeparator();
    }

    static ToolStripItem BuildDelete(TrackedDelete delete, Action accept)
    {
        var menu = new ToolStripDropDownButton($"{delete.Name}")
        {
            DropDownDirection = ToolStripDropDownDirection.Left
        };
        menu.DropDownItems.Add(new MenuButton("Accept delete", accept));
        menu.DropDownItems.Add(BuildShowInExplorer(delete.File));
        return menu;
    }

    static ToolStripItem BuildMove(TrackedMove move, Action accept, Action discard)
    {
        var tempName = Path.GetFileNameWithoutExtension(move.Temp);
        var targetName = Path.GetFileNameWithoutExtension(move.Target);
        var text = GetMoveText(move, tempName, targetName);
        var menu = new ToolStripDropDownButton(text)
        {
            DropDownDirection = ToolStripDropDownDirection.Left
        };
        menu.DropDownItems.Add(new MenuButton("Accept move", accept));
        menu.DropDownItems.Add(new MenuButton("Discard", discard));
        if (move.Exe != null)
        {
            menu.DropDownItems.Add(new MenuButton("Open diff tool", () => DiffToolLauncher.Launch(move)));
        }

        menu.DropDownItems.Add(BuildShowInExplorer(move.Temp));
        return menu;
    }

    static string GetMoveText(TrackedMove move, string tempName, string targetName)
    {
        if (Path.GetFileNameWithoutExtension(tempName) == Path.GetFileNameWithoutExtension(targetName))
        {
            return $"{move.Name} ({move.Extension})";
        }

        return $"{tempName} > {targetName} ({move.Extension})";
    }

    static MenuButton BuildShowInExplorer(string file) =>
        new("Open directory", () => ExplorerLauncher.ShowFileInExplorer(file));
}