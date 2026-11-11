/// <summary>
/// PlatformHeads' <see cref="RuntimeWatch" /> for the tray, logging to the tray's log: a servicing
/// update deleted the runtime folder the tray started on, and it went on running with no icon,
/// dropping every move a test run sent, while its mutex kept a new tray out.
/// </summary>
static class TrayRuntime
{
    /// <param name="exit">Ends the tray as its Exit does. Called on a thread pool thread.</param>
    public static RuntimeWatch? Watch(Action exit)
    {
        var watch = RuntimeWatch.Start(
            exit,
            told: _ => Log.Warning("{Message}", _),
            failed: _ => Log.Error(_, "Failed while getting ready to start the tray again if its runtime is removed"));
        if (watch is null)
        {
            Log.Information("Not started by a command of its own, so the tray will not start itself again if its runtime is removed");
        }

        return watch;
    }
}
