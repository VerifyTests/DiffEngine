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

        var fixedItems = TrayMenu.Fixed(head);
        var tracked = TrayMenu.Tracked(tracker, head);
        while (!cancel.IsCancellationRequested)
        {
            if (tray.Poll().ClickedItem is { } id)
            {
                Invoke(fixedItems, tracked, id);
            }

            fixedItems = TrayMenu.Fixed(head);
            tracked = TrayMenu.Tracked(tracker, head);
            tray.Apply(new(icon(), "DiffEngineTray", [.. fixedItems.Items, .. tracked.Items]));
            await Wait(turn, cancel);
        }
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
