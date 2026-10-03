/// <summary>
/// One context menu item. Every command acts on the current selection — opening a menu on an
/// entry selects it first — or, for the group commands, on <see cref="MenuState.Members"/>.
/// </summary>
record MenuItem(string Label, CommandKind Kind);

/// <summary>
/// The open context menu. <paramref name="Row"/> anchors it in the full row projection;
/// <paramref name="Members"/> are the queue indexes a group command sweeps. Closed by any other
/// input, and by anything that changes the queue, because the indexes describe the queue the menu
/// was opened over.
/// </summary>
record MenuState(int Row, IReadOnlyList<MenuItem> Items, IReadOnlyList<int> Members)
{
    /// <summary>
    /// The group this menu can fold, null on an entry menu. Read the way
    /// <see cref="Members"/> is: captured when the menu opens, so the command still knows what it
    /// was opened over after the menu has closed.
    /// </summary>
    public string? GroupKey { get; init; }

    /// <summary>
    /// The pane this menu was opened over, null for one opened on a queue row. A pane's menu hangs
    /// where the pointer was rather than under a row, which only the head that took the click
    /// knows, so <paramref name="Row"/> is -1 for one. Captured for the same reason
    /// <see cref="GroupKey"/> is: select all has to know which pane it was asked in.
    /// </summary>
    public PaneSide? Pane { get; init; }
}

/// <summary>
/// What a right-click on each kind of row offers. Content only — where menus open and close is
/// <see cref="ViewerSession"/>'s business — so the offering is testable as plain data.
/// </summary>
static class ContextMenu
{
    public static IReadOnlyList<MenuItem> ForEntry(QueueEntry entry, bool hasSelection)
    {
        var items = new List<MenuItem>();
        switch (entry.Kind)
        {
            case QueueEntryKind.Move:
                items.Add(new("Accept move", CommandKind.Accept));
                items.Add(new("Discard", CommandKind.Discard));
                items.Add(new("Open target directory", CommandKind.RevealSource));
                break;
            case QueueEntryKind.Delete:
                items.Add(new("Accept delete", CommandKind.Accept));
                items.Add(new("Discard", CommandKind.Discard));
                items.Add(new("Open directory", CommandKind.RevealSource));
                break;
            default:
                items.Add(new("Accept", CommandKind.Accept));
                if (entry.Conflicted)
                {
                    items.Add(new("Show next variant", CommandKind.NextVariant));
                }

                items.Add(new("Discard", CommandKind.Discard));
                items.Add(new("Open source file", CommandKind.RevealSource));
                break;
        }

        // Last, and named after the panes rather than after the sides, so the menu reads as the
        // headers above the text it copies. "Copy selection" only when there is one: an item that
        // would copy nothing is worse than no item.
        if (hasSelection)
        {
            items.Add(new("Copy selection", CommandKind.Copy));
        }

        AddCopy(items, entry, PaneSide.Left, CommandKind.CopyLeft);
        AddCopy(items, entry, PaneSide.Right, CommandKind.CopyRight);
        return items;
    }

    /// <summary>
    /// What a right-click on a pane's text offers: the copying a reader would otherwise have to
    /// know the keys for. Nothing that acts on the entry, which the queue row's menu and the
    /// footer already do, so a click meant to copy cannot land on an accept.
    /// <para>
    /// Empty when there is nothing in the pane to copy or select, which is no menu at all rather
    /// than one of items that do nothing.
    /// </para>
    /// </summary>
    /// <param name="selectable">
    /// Whether the rows on screen are the entry's text, which is what a selection is held in. A
    /// document seen as its pages shows rows describing it instead, and nothing is selected there.
    /// </param>
    public static IReadOnlyList<MenuItem> ForPane(QueueEntry entry, PaneSide side, bool hasSelection, bool selectable)
    {
        var items = new List<MenuItem>();
        if (hasSelection)
        {
            items.Add(new("Copy selection", CommandKind.Copy));
        }

        if (SelectionText.Any(entry, side))
        {
            items.Add(new("Copy all", side == PaneSide.Left ? CommandKind.CopyLeft : CommandKind.CopyRight));
            if (selectable)
            {
                items.Add(new("Select all", CommandKind.SelectAll));
            }
        }

        return items;
    }

    /// <summary>
    /// A side, unless copying it would copy nothing. A pending delete's left side is the state
    /// after accepting, which is no file at all, and the expected side of a brand new snapshot has
    /// nothing in it yet - and an item that reports "nothing to copy" is worse than no item.
    /// </summary>
    static void AddCopy(List<MenuItem> items, QueueEntry entry, PaneSide side, CommandKind kind)
    {
        if (SelectionText.Any(entry, side))
        {
            items.Add(new($"Copy {SelectionText.Header(entry, side)}", kind));
        }
    }

    /// <summary>
    /// Folding leads, above the two bulk commands. It is the only item here that changes nothing
    /// but the view, and it is the one reached most often, so it takes the position nearest the
    /// pointer and pushes "discard all" away from it.
    /// </summary>
    public static IReadOnlyList<MenuItem> ForSolution(string name, bool collapsed) =>
    [
        Fold(collapsed),
        new($"Accept all in {name}", CommandKind.AcceptGroup),
        new($"Discard all in {name}", CommandKind.DiscardGroup)
    ];

    public static IReadOnlyList<MenuItem> ForTest(string name, bool collapsed) =>
    [
        Fold(collapsed),
        new($"Accept all for {name}", CommandKind.AcceptGroup),
        new($"Discard all for {name}", CommandKind.DiscardGroup)
    ];

    static MenuItem Fold(bool collapsed) =>
        new(collapsed ? "Expand" : "Collapse", CommandKind.ToggleGroup);
}
