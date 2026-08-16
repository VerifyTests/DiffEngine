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
        items.Add(new MenuButton("Debug view", async () => await DebugFormLauncher.Launch(tracker), Images.Options));
        items.Add(new MenuButton("Open logs", Logging.OpenDirectory, Images.Folder));
        items.Add(new MenuButton("Purge verified files", FilePurger.Launch, Images.Folder));
        items.Add(new MenuButton("Raise issue", IssueLauncher.Launch, Images.Link));
        return menu;
    }

    /// <summary>
    /// The items that survive a close, matched by text. The tracked ones are rebuilt from scratch
    /// every time the menu opens, so anything added in <see cref="Build"/> has to be listed here
    /// too or it is removed the first time the menu closes.
    /// </summary>
    static readonly string[] fixedItems =
    [
        "Exit",
        "Options",
        "Debug view",
        "Open logs",
        "Purge verified files",
        "Raise issue"
    ];

    static List<ToolStripItem> NonDefaultMenus(ToolStripItemCollection items) =>
        items
            .Cast<ToolStripItem>()
            .Where(_ => !fixedItems.Contains(_.Text))
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
        // Read everything first and decide from the counts. TrackingAny is backed by the scan
        // cache, which drives the icon, and the snapshot half of it can be up to one scan behind
        // what the viewer actually has queued.
        var deletes = tracker
            .Deletes
            .OrderBy(_ => _.File)
            .ToList();

        var moves = tracker
            .Moves
            .OrderBy(_ => _.Temp)
            .ToList();

        var snapshots = tracker
            .Snapshots
            .OrderBy(_ => _.Name)
            .ToList();

        var count = moves.Count + deletes.Count + snapshots.Count;
        if (count == 0)
        {
            yield break;
        }

        yield return new ToolStripSeparator();

        foreach (var item in BuildGroupedMenuItems(tracker, deletes, moves, snapshots))
        {
            yield return item;
        }

        // Closing the viewer is a process-wide action, not something done to one group's
        // snapshots, so it sits on its own rather than trailing the last group that happens to
        // have some.
        if (snapshots.Count != 0)
        {
            yield return new MenuButton("Close snapshot viewer", tracker.CloseViewer);
            yield return new ToolStripSeparator();
        }

        yield return new MenuButton($"Discard ({count})", tracker.Clear, Images.Discard);
        yield return new MenuButton($"Accept all ({count})", () => tracker.AcceptAll(), Images.AcceptAll);
    }

    static IEnumerable<ToolStripItem> BuildGroupedMenuItems(
        Tracker tracker,
        List<TrackedDelete> deletes,
        List<TrackedMove> moves,
        List<PendingSnapshot> snapshots)
    {
        var groups = deletes
            .Select(_ => _.Group)
            .Concat(moves.Select(_ => _.Group))
            .Concat(snapshots.Select(_ => _.Group))
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
                             .ToList(),
                         snapshots
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

    static IEnumerable<ToolStripItem> BuildMovesAndDeletes(
        string? name,
        Tracker tracker,
        List<TrackedDelete> deletes,
        List<TrackedMove> moves,
        List<PendingSnapshot> snapshots)
    {
        if (name != null)
        {
            yield return new MenuButton(name, null, Images.VisualStudio);
        }

        if (deletes.Count != 0)
        {
            yield return new MenuButton(
                $"Pending Deletes ({deletes.Count}):",
                () => tracker.Accept(deletes),
                Images.Delete);
            foreach (var delete in deletes)
            {
                yield return BuildDelete(delete, () => tracker.Accept(delete));
            }
        }

        if (moves.Count != 0)
        {
            yield return new MenuButton(
                $"Pending Moves ({moves.Count}):",
                () => tracker.Accept(moves),
                Images.Accept);
            foreach (var move in moves)
            {
                yield return BuildMove(
                    move,
                    () => tracker.Accept(move),
                    () => tracker.Discard(move));
            }
        }

        if (snapshots.Count != 0)
        {
            // The group's own list, matching the deletes and moves headers above: solution A's
            // header must not accept solution B's queue.
            yield return new MenuButton(
                $"Pending Snapshots ({snapshots.Count}):",
                () => tracker.Accept(snapshots),
                Images.Accept);
            foreach (var snapshot in snapshots)
            {
                yield return BuildSnapshot(
                    snapshot,
                    () => tracker.Accept(snapshot),
                    () => tracker.Discard(snapshot),
                    () => tracker.Focus(snapshot));
            }
        }

        yield return new ToolStripSeparator();
    }

    static ToolStripDropDownButton BuildSnapshot(PendingSnapshot snapshot, Action accept, Action discard, Action focus)
    {
        var failed = snapshot.Status == null ? "" : " !";
        var menu = new ToolStripDropDownButton($"{snapshot.Name} (inline){failed}")
        {
            DropDownDirection = ToolStripDropDownDirection.Left
        };
        if (snapshot.Status is { } status)
        {
            // The marker said something had gone wrong and the menu had nowhere to say what. An
            // accept reporting "13 not written" over thirteen bare exclamation marks tells a
            // reader only that they are on their own. On the item itself as well as in the tip,
            // because a reason that has to be hovered for is a reason most people never see
            menu.ToolTipText = status;
            menu.DropDownItems.Add(new ToolStripLabel(Shorten(status))
            {
                Enabled = false,
                ToolTipText = status
            });
        }

        menu.DropDownItems.Add(new MenuButton("Accept snapshot", accept));
        menu.DropDownItems.Add(new MenuButton("Discard", discard));
        // Replaces "Open diff tool": the viewer is the diff tool, so the useful action is to bring
        // it forward on this item, starting one if the queue is here and nothing is showing it.
        menu.DropDownItems.Add(new MenuButton("Open in viewer", focus));
        menu.DropDownItems.Add(new MenuButton("Open source file", () => ExplorerLauncher.ShowFileInExplorer(snapshot.Source)));
        return menu;
    }

    /// <summary>
    /// Enough of the reason to act on, at a width a menu can hold. A tray menu grows to its widest
    /// item, so the untrimmed text would drag the whole thing across the screen. The tip beside it
    /// carries the rest, which is the right way round: the item says what happened, and hovering is
    /// for the reader who wants the sentence finished.
    /// </summary>
    static string Shorten(string status) =>
        status.Length <= 70 ? status : $"{status[..69].TrimEnd()}…";

    static ToolStripDropDownButton BuildDelete(TrackedDelete delete, Action accept)
    {
        var menu = new ToolStripDropDownButton($"{delete.Name}")
        {
            DropDownDirection = ToolStripDropDownDirection.Left
        };
        menu.DropDownItems.Add(new MenuButton("Accept delete", accept));
        menu.DropDownItems.Add(BuildShowInExplorer(delete.File));
        return menu;
    }

    static ToolStripDropDownButton BuildMove(TrackedMove move, Action accept, Action discard)
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