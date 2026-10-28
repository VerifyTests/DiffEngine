/// <summary>
/// The tray's menu as a model: PlatformHeads' <see cref="TrayMenuItem" />s for a head to show, and
/// what each one does when it is chosen. What it says is a function of what the tracker holds, the
/// same on every head, and is snapshot tested as text. It was built as ToolStrip items, which only
/// the Windows head could show and only a picture could test.
/// <para>
/// In two halves, because the Windows head keeps the fixed items on its menu and builds the tracked
/// ones each time the menu opens (PlatformHeads' MenuOnOpening).
/// </para>
/// <para>
/// An item's id names what it acts on by that entry's key: a move's received file, a delete's file,
/// a snapshot's key. A menu shown from one build and chosen from after another can then act only on
/// the entry it names, never on whichever one came to sit in its place.
/// </para>
/// </summary>
sealed class TrayMenu
{
    readonly List<TrayMenuItem> items = [];
    readonly Dictionary<string, Action> actions = [];
    readonly Dictionary<string, string> tooltips = [];
    int separators;

    public IReadOnlyList<TrayMenuItem> Items => items;

    /// <summary>
    /// What a hover over the item says, where a head shows one, or null. Beside the model rather
    /// than in it, since a Linux tray's menu has nowhere to show one.
    /// </summary>
    public string? Tooltip(string id) =>
        tooltips.GetValueOrDefault(id);

    /// <summary>
    /// Does what the item does. False for an id this menu did not build, which is how a head that
    /// holds both halves finds the one an item came from.
    /// </summary>
    public bool Invoke(string id)
    {
        if (!actions.TryGetValue(id, out var action))
        {
            return false;
        }

        action();
        return true;
    }

    public static TrayMenu Fixed(TrayActions head)
    {
        var menu = new TrayMenu();
        menu.items.AddRange(
        [
            menu.Button("exit", "Exit", head.Exit, "exit"),
            menu.Button("options", "Options", head.Options, "options"),
            menu.Button("debug", "Debug view", head.DebugView, "options"),
            menu.Button("logs", "Open logs", Logging.OpenDirectory, "folder"),
            menu.Button("purge", "Purge verified files", head.Purge, "folder"),
            menu.Button("issue", "Raise issue", IssueLauncher.Launch, "link")
        ]);
        return menu;
    }

    public static TrayMenu Tracked(Tracker tracker, TrayActions head)
    {
        var menu = new TrayMenu();
        menu.AddTracked(tracker, head);
        return menu;
    }

    void AddTracked(Tracker tracker, TrayActions head)
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
            return;
        }

        items.Add(Separator());

        AddGroups(tracker, head, deletes, moves, snapshots);

        // Closing the viewer is a process-wide action, not something done to one group's
        // snapshots, so it sits on its own rather than trailing the last group that happens to
        // have some.
        if (snapshots.Count != 0)
        {
            items.Add(Button("viewer:close", "Close snapshot viewer", tracker.CloseViewer));
            items.Add(Separator());
        }

        items.Add(Button("discard", $"Discard ({count})", () => tracker.Clear(), "discard"));
        items.Add(Button("accept", $"Accept all ({count})", () => tracker.AcceptAll(), "acceptAll"));
    }

    void AddGroups(
        Tracker tracker,
        TrayActions head,
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
            foreach (var item in Group(
                         group,
                         tracker,
                         head,
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
                items.Add(item);
                addedCount++;
                if (addedCount == 20)
                {
                    items.Add(Label("limit", "Only 20 items rendered"));
                    return;
                }
            }
        }
    }

    IEnumerable<TrayMenuItem> Group(
        string? name,
        Tracker tracker,
        TrayActions head,
        List<TrackedDelete> deletes,
        List<TrackedMove> moves,
        List<PendingSnapshot> snapshots)
    {
        if (name != null)
        {
            // A heading, so not something to choose. On the ToolStrip menu it was an item that
            // could not be selected, which a menu built from the model has no way to say, and an
            // item that does nothing when chosen is a trap on any platform's menu
            yield return Label($"group:{name}", name, "solution");
        }

        if (deletes.Count != 0)
        {
            yield return Button(
                $"deletes:{name}",
                $"Pending Deletes ({deletes.Count}):",
                () => tracker.Accept(deletes),
                "delete");
            foreach (var delete in deletes)
            {
                yield return Delete(delete, tracker.HeldReason(delete), () => tracker.Accept(delete));
            }
        }

        if (moves.Count != 0)
        {
            yield return Button(
                $"moves:{name}",
                $"Pending Moves ({moves.Count}):",
                () => tracker.Accept(moves),
                "accept");
            foreach (var move in moves)
            {
                yield return Move(
                    move,
                    () => tracker.Accept(move),
                    () => tracker.Discard(move),
                    () =>
                    {
                        if (tracker.FindMove(move.Temp) is {Exe: not null} live)
                        {
                            head.OpenDiffTool(live);
                        }
                    });
            }
        }

        if (snapshots.Count != 0)
        {
            // The group's own list, matching the deletes and moves headers above: solution A's
            // header must not accept solution B's queue.
            yield return Button(
                $"snapshots:{name}",
                $"Pending Snapshots ({snapshots.Count}):",
                () => tracker.Accept(snapshots),
                "accept");
            foreach (var snapshot in snapshots)
            {
                yield return Snapshot(
                    snapshot,
                    () => tracker.Accept(snapshot),
                    () => tracker.Discard(snapshot),
                    () => tracker.Focus(snapshot));
            }
        }

        yield return Separator();
    }

    TrayMenuItem Snapshot(PendingSnapshot snapshot, Action accept, Action discard, Action focus)
    {
        var id = $"snapshot:{snapshot.Key}";
        var failed = snapshot.Status == null ? "" : " !";
        List<TrayMenuItem> children = [];
        if (snapshot.Status is { } status)
        {
            // The marker said something had gone wrong and the menu had nowhere to say what. An
            // accept reporting "13 not written" over thirteen bare exclamation marks tells a
            // reader only that they are on their own. On the item itself as well as in the tip,
            // because a reason that has to be hovered for is a reason most people never see.
            // An item rather than a label on the ToolStrip menu: a label does not count towards
            // the drop down's width, so its text was clipped to whatever the buttons below
            // happened to make the menu - about twenty characters of a sentence
            tooltips[id] = status;
            var statusId = $"{id}:status";
            tooltips[statusId] = status;
            children.Add(Label(statusId, ForMenu(snapshot.Name, status)));
        }

        children.Add(Button($"{id}:accept", "Accept snapshot", accept));
        children.Add(Button($"{id}:discard", "Discard", discard));
        // Replaces "Open diff tool": the viewer is the diff tool, so the useful action is to bring
        // it forward on this item, starting one if the queue is here and nothing is showing it.
        children.Add(Button($"{id}:viewer", "Open in viewer", focus));
        children.Add(Button($"{id}:source", "Open source file", () => DirectoryLauncher.Reveal(snapshot.Source)));
        return new(id, $"{snapshot.Name} (inline){failed}", Children: children);
    }

    /// <summary>
    /// One line of it, and the part worth the room. The status leads with the name of the snapshot,
    /// which the item this hangs under has just said, so the prefix goes and the reason itself gets
    /// the width.
    /// <para>
    /// One line rather than a wrapped block: a drop down gives every item the height of its tallest,
    /// so a reason over three lines pushed Accept, Discard and the rest three lines apart each. The
    /// tip carries the sentence whole for anyone who wants the end of it.
    /// </para>
    /// </summary>
    static string ForMenu(string name, string status)
    {
        var prefix = $"{name} not written. ";
        if (status.StartsWith(prefix, StringComparison.Ordinal))
        {
            status = status[prefix.Length..];
        }

        return Shortened(status);
    }

    static string Shortened(string text) =>
        text.Length <= 60 ? text : $"{text[..59].TrimEnd()}…";

    /// <summary>
    /// What a delete "Accept all" would leave pending is marked with. Not the <c>!</c> of a
    /// snapshot that failed to apply, which it used to share: nothing failed here, and the delete
    /// is waiting to be accepted on its own. The character the viewer's queue column leads a held
    /// delete's row with.
    /// </summary>
    internal const string HeldMark = " ~";

    /// <param name="delete">The pending delete.</param>
    /// <param name="held">
    /// Why "Accept all" leaves this delete pending, when it does. Marked, and said the way a
    /// snapshot that was not written says why: a delete an accept-all had just passed over looked
    /// like every other, and pressing "Accept all" again was the natural thing to try.
    /// </param>
    /// <param name="accept">Accepts this delete on its own.</param>
    TrayMenuItem Delete(TrackedDelete delete, string? held, Action accept)
    {
        var id = $"delete:{delete.File}";
        var marker = held == null ? "" : HeldMark;
        List<TrayMenuItem> children = [];
        if (held != null)
        {
            tooltips[id] = held;
            var heldId = $"{id}:held";
            tooltips[heldId] = held;
            children.Add(Label(heldId, Shortened(held)));
        }

        children.Add(Button($"{id}:accept", "Accept delete", accept));
        children.Add(OpenDirectory(id, delete.File));
        return new(id, $"{delete.Name}{marker}", Children: children);
    }

    /// <param name="openDiffTool">
    /// Finds the move again by its received file when the item is chosen. A re-run replaces the
    /// move while a menu built before it can still be open, and the one captured here then holds a
    /// process the replacement has disposed.
    /// </param>
    TrayMenuItem Move(TrackedMove move, Action accept, Action discard, Action openDiffTool)
    {
        var id = $"move:{move.Temp}";
        List<TrayMenuItem> children =
        [
            Button($"{id}:accept", "Accept move", accept),
            Button($"{id}:discard", "Discard", discard)
        ];
        if (move.Exe != null)
        {
            children.Add(Button($"{id}:tool", "Open diff tool", openDiffTool));
        }

        children.Add(OpenDirectory(id, move.Temp));
        return new(id, MoveText(move), Children: children);
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

    TrayMenuItem OpenDirectory(string id, string file) =>
        Button($"{id}:directory", "Open directory", () => DirectoryLauncher.Reveal(file));

    TrayMenuItem Button(string id, string label, Action action, string? icon = null)
    {
        actions.Add(id, action);
        return new(id, label, IconName: icon);
    }

    static TrayMenuItem Label(string id, string label, string? icon = null) =>
        new(id, label, Enabled: false, IconName: icon);

    TrayMenuItem Separator()
    {
        separators++;
        return new($"separator:{separators}", "", Separator: true);
    }
}
