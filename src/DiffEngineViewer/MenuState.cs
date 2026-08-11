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
record MenuState(int Row, IReadOnlyList<MenuItem> Items, IReadOnlyList<int> Members);

/// <summary>
/// What a right-click on each kind of row offers. Content only — where menus open and close is
/// <see cref="ViewerSession"/>'s business — so the offering is testable as plain data.
/// </summary>
static class ContextMenu
{
    public static IReadOnlyList<MenuItem> ForEntry(QueueEntry entry)
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

        return items;
    }

    public static IReadOnlyList<MenuItem> ForSolution(string name) =>
    [
        new($"Accept all in {name}", CommandKind.AcceptGroup),
        new($"Discard all in {name}", CommandKind.DiscardGroup)
    ];

    public static IReadOnlyList<MenuItem> ForTest(string name) =>
    [
        new($"Accept all for {name}", CommandKind.AcceptGroup),
        new($"Discard all for {name}", CommandKind.DiscardGroup)
    ];
}
