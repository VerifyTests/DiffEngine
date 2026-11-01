/// <summary>
/// The turns of <see cref="TrayLoop" />, for whichever loop runs them: the menus last shown, so a
/// click is done on the menu it was chosen from and a tip is the one for the item shown. The
/// Windows head runs them from a timer on its message loop, where the tray's events are raised.
/// </summary>
sealed class TrayTurns
{
    readonly ITray<TrayIcon> tray;
    readonly Tracker tracker;
    readonly TrayActions head;
    readonly Func<TrayIcon> icon;
    TrayMenu fixedItems;
    TrayMenu tracked;

    /// <summary>
    /// Shows the menus at once, so the tray holds them before anyone can open it.
    /// </summary>
    public TrayTurns(ITray<TrayIcon> tray, Tracker tracker, TrayActions head, Func<TrayIcon> icon)
    {
        this.tray = tray;
        this.tracker = tracker;
        this.head = head;
        this.icon = icon;
        (fixedItems, tracked) = Show();
    }

    /// <summary>
    /// What was chosen on the menus last shown done, and the menus shown again. Done on those
    /// menus: an item's id names what it acts on, so one built since would act on the same entry,
    /// but an item the tracker has since dropped is in no other.
    /// </summary>
    public void Turn()
    {
        if (tray.Poll().ClickedItem is { } id)
        {
            Invoke(id);
        }

        (fixedItems, tracked) = Show();
    }

    /// <summary>
    /// The tip of an item on the menus last shown, which a tray whose menus can show one asks as
    /// it builds them.
    /// </summary>
    public string? Tooltip(string id) =>
        fixedItems.Tooltip(id) ?? tracked.Tooltip(id);

    (TrayMenu Fixed, TrayMenu Tracked) Show()
    {
        var shownFixed = TrayMenu.Fixed(head);
        var shownTracked = TrayMenu.Tracked(tracker, head);
        tray.Apply(new(icon(), "DiffEngineTray", [.. shownFixed.Items, .. shownTracked.Items]));
        return (shownFixed, shownTracked);
    }

    void Invoke(string id)
    {
        try
        {
            if (!fixedItems.Invoke(id))
            {
                tracked.Invoke(id);
            }
        }
        catch (Exception exception)
        {
            // Thrown out of here, it would end the loop, and with it the tray
            ExceptionHandler.Handle($"Failed to do what the menu item {id} does", exception);
        }
    }
}
