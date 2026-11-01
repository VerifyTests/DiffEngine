/// <summary>
/// The loop of a head whose tray is a model: the menu built from the tracker and handed to the
/// tray, and what was chosen on it done, on the loop's own thread.
/// <para>
/// Built every turn rather than as the menu opens. A StatusNotifierItem's menu is the panel's to
/// show, and the panel's question before it shows one, dbusmenu's AboutToShow, PlatformHeads
/// answers itself with "nothing changed" and tells the app nothing, so the tray has to hold the
/// current layout before anyone opens it. A turn that changes nothing hands the tray a model equal
/// to the one it holds, which it sends nowhere. The Windows head built its menu as it opened, and
/// takes the same turns now, from a timer on its message loop (<see cref="TrayTurns" />), so a
/// menu can be a turn behind the tracker, which the ids make safe: see <see cref="TrayTurns.Turn" />.
/// </para>
/// </summary>
static class TrayLoop
{
    /// <summary>
    /// How long a click waits to be done, and how stale the menu can be.
    /// </summary>
    public static readonly TimeSpan Turn = TimeSpan.FromMilliseconds(100);

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

        var turns = new TrayTurns(tray, tracker, head, icon);
        while (!cancel.IsCancellationRequested)
        {
            await Wait(Turn, cancel);
            turns.Turn();
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
        var turns = new TrayTurns(tray, tracker, head, icon);
        while (!cancel.IsCancellationRequested)
        {
            pump();
            turns.Turn();
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
