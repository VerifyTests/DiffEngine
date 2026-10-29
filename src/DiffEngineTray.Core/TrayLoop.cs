/// <summary>
/// The loop of a head whose tray is a model and no window: the menu built from the tracker and
/// handed to the tray, and what was chosen on it done, on the loop's own thread.
/// <para>
/// Built every turn rather than as the menu opens, which is how the Windows head builds it. A
/// StatusNotifierItem's menu is the panel's to show, and the panel's question before it shows one,
/// dbusmenu's AboutToShow, PlatformHeads answers itself with "nothing changed" and tells the app
/// nothing, so the tray has to hold the current layout before anyone opens it. A turn that changes
/// nothing hands the tray a model equal to the one it holds, which it sends nowhere.
/// </para>
/// </summary>
static class TrayLoop
{
    /// <summary>
    /// How long a click waits to be done, and how stale the menu can be.
    /// </summary>
    static readonly TimeSpan turn = TimeSpan.FromMilliseconds(100);

    /// <param name="tray">
    /// Null where there is nowhere to show the menu, as on a GNOME without the AppIndicator
    /// extension. The tray still holds the queue and tracks what test runs send it, until the
    /// session ends it.
    /// </param>
    public static async Task Run(ITray<TrayIcon>? tray, Tracker tracker, TrayActions head, Func<TrayIcon> icon, Cancel cancel)
    {
        if (tray is null)
        {
            await Wait(Timeout.InfiniteTimeSpan, cancel);
            return;
        }

        var shown = Show(tray, tracker, head, icon);
        while (!cancel.IsCancellationRequested)
        {
            await Wait(turn, cancel);
            shown = Turn(tray, shown, tracker, head, icon);
        }
    }

    /// <summary>
    /// The same turns for a tray that has to be driven from the thread it was opened on, as
    /// AppKit has to be from the main thread: on the calling thread throughout, with the tray's
    /// own pump as the wait, which is how its events are dispatched at all. An await would carry
    /// the next turn to the pool.
    /// </summary>
    public static void RunPumped(ITray<TrayIcon> tray, Action pump, Tracker tracker, TrayActions head, Func<TrayIcon> icon, Cancel cancel)
    {
        var shown = Show(tray, tracker, head, icon);
        while (!cancel.IsCancellationRequested)
        {
            pump();
            shown = Turn(tray, shown, tracker, head, icon);
        }
    }

    /// <summary>
    /// What was chosen on the menus last shown done, and the menus shown again.
    /// </summary>
    static (TrayMenu Fixed, TrayMenu Tracked) Turn(
        ITray<TrayIcon> tray,
        (TrayMenu Fixed, TrayMenu Tracked) shown,
        Tracker tracker,
        TrayActions head,
        Func<TrayIcon> icon)
    {
        if (tray.Poll().ClickedItem is { } id)
        {
            Invoke(shown.Fixed, shown.Tracked, id);
        }

        return Show(tray, tracker, head, icon);
    }

    static (TrayMenu Fixed, TrayMenu Tracked) Show(ITray<TrayIcon> tray, Tracker tracker, TrayActions head, Func<TrayIcon> icon)
    {
        var fixedItems = TrayMenu.Fixed(head);
        var tracked = TrayMenu.Tracked(tracker, head);
        tray.Apply(new(icon(), "DiffEngineTray", [.. fixedItems.Items, .. tracked.Items]));
        return (fixedItems, tracked);
    }

    /// <summary>
    /// On the menus the item was shown from. Its id names what it acts on, so one built since
    /// would act on the same entry, but an item the tracker has since dropped is in no other.
    /// </summary>
    static void Invoke(TrayMenu fixedItems, TrayMenu tracked, string id)
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

    static async Task Wait(TimeSpan time, Cancel cancel)
    {
        try
        {
            await Task.Delay(time, cancel);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
