namespace DiffEngineTray;

// Builds the tray NativeMenu from the Tracker, mirroring the Windows Forms MenuBuilder.
static class TrayMenuBuilder
{
    public static NativeMenu Build(TrayCommands commands, Tracker tracker)
    {
        var menu = new NativeMenu();

        // Wrap a tracker-mutating action so the menu rebuilds immediately afterwards
        // (macOS native menus do not re-query on open).
        ICommand Mutating(Action action) =>
            new ActionCommand(() =>
            {
                action();
                commands.Refresh();
            });

        if (tracker.TrackingAny)
        {
            var deletes = tracker
                .Deletes
                .OrderBy(_ => _.File)
                .ToList();
            var moves = tracker
                .Moves
                .OrderBy(_ => _.Temp)
                .ToList();
            var count = moves.Count + deletes.Count;

            foreach (var item in BuildGrouped(tracker, deletes, moves, Mutating))
            {
                menu.Items.Add(item);
            }

            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(new NativeMenuItem($"Discard ({count})") { Command = Mutating(tracker.Clear) });
            menu.Items.Add(new NativeMenuItem($"Accept all ({count})") { Command = Mutating(tracker.AcceptAll) });
            menu.Items.Add(new NativeMenuItemSeparator());
        }

        menu.Items.Add(new NativeMenuItem("Options") { Command = new ActionCommand(commands.Options) });
        menu.Items.Add(new NativeMenuItem("Open logs") { Command = new ActionCommand(commands.OpenLogs) });
        menu.Items.Add(new NativeMenuItem("Purge verified files") { Command = new ActionCommand(commands.Purge) });
        menu.Items.Add(new NativeMenuItem("Raise issue") { Command = new ActionCommand(commands.RaiseIssue) });
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(new NativeMenuItem("Exit") { Command = new ActionCommand(commands.Exit) });

        return menu;
    }

    static IEnumerable<NativeMenuItem> BuildGrouped(
        Tracker tracker,
        List<TrackedDelete> deletes,
        List<TrackedMove> moves,
        Func<Action, ICommand> mutating)
    {
        var groups = deletes
            .Select(_ => _.Group)
            .Concat(moves.Select(_ => _.Group))
            .Distinct()
            .ToList();

        var addedCount = 0;
        foreach (var group in groups)
        {
            var groupDeletes = deletes
                .Where(_ => _.Group == group)
                .ToList();
            var groupMoves = moves
                .Where(_ => _.Group == group)
                .ToList();

            if (group != null)
            {
                yield return Disabled(group);
            }

            if (groupDeletes.Count != 0)
            {
                yield return new($"Pending Deletes ({groupDeletes.Count}):")
                {
                    Command = mutating(() => tracker.Accept(groupDeletes))
                };
                foreach (var delete in groupDeletes)
                {
                    yield return BuildDelete(tracker, delete, mutating);
                    if (++addedCount == 20)
                    {
                        yield return Disabled("Only 20 items rendered");
                        yield break;
                    }
                }
            }

            if (groupMoves.Count != 0)
            {
                yield return new($"Pending Moves ({groupMoves.Count}):")
                {
                    Command = mutating(() => tracker.Accept(groupMoves))
                };
                foreach (var move in groupMoves)
                {
                    yield return BuildMove(tracker, move, mutating);
                    if (++addedCount == 20)
                    {
                        yield return Disabled("Only 20 items rendered");
                        yield break;
                    }
                }
            }
        }
    }

    static NativeMenuItem BuildDelete(Tracker tracker, TrackedDelete delete, Func<Action, ICommand> mutating)
    {
        var submenu = new NativeMenu();
        submenu.Items.Add(new NativeMenuItem("Accept delete") { Command = mutating(() => tracker.Accept(delete)) });
        submenu.Items.Add(new NativeMenuItem("Open directory") { Command = new ActionCommand(() => TrayServices.RevealFile(delete.File)) });
        return new(delete.Name)
        {
            Menu = submenu
        };
    }

    static NativeMenuItem BuildMove(Tracker tracker, TrackedMove move, Func<Action, ICommand> mutating)
    {
        var submenu = new NativeMenu();
        submenu.Items.Add(new NativeMenuItem("Accept move") { Command = mutating(() => tracker.Accept(move)) });
        submenu.Items.Add(new NativeMenuItem("Discard") { Command = mutating(() => tracker.Discard(move)) });
        if (move.Exe != null)
        {
            submenu.Items.Add(new NativeMenuItem("Open diff tool") { Command = new ActionCommand(() => DiffToolLauncher.Launch(move)) });
        }

        submenu.Items.Add(new NativeMenuItem("Open directory") { Command = new ActionCommand(() => TrayServices.RevealFile(move.Temp)) });
        return new(MoveText(move))
        {
            Menu = submenu
        };
    }

    static string MoveText(TrackedMove move)
    {
        var tempName = Path.GetFileNameWithoutExtension(move.Temp);
        var targetName = Path.GetFileNameWithoutExtension(move.Target);
        if (Path.GetFileNameWithoutExtension(tempName) == Path.GetFileNameWithoutExtension(targetName))
        {
            return $"{move.Name} ({move.Extension})";
        }

        return $"{tempName} > {targetName} ({move.Extension})";
    }

    static NativeMenuItem Disabled(string header) =>
        new(header)
        {
            IsEnabled = false
        };
}
