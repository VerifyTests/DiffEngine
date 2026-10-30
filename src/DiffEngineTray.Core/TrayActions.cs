/// <summary>
/// What the tray's menu does that only a head can: show the head's own windows, end the app, and
/// start a move's diff tool, which on Windows brings the tool's window forward through user32.
/// </summary>
/// <param name="Options">The head's options window, or null for a head with none.</param>
/// <param name="Purge">The head's purge, or null for a head with no folder picker to start it.</param>
/// <param name="OpenDiffTool">Handed the move as it is when the item is chosen, with an executable.</param>
/// <param name="RunAtLogin">
/// For a head with no options window, run at login as a checked item on the menu, which is the one
/// option that does anything off Windows: the hot keys are Windows', and target on left and the
/// launch limit are user environment variables, which .NET sets only there.
/// </param>
/// <param name="Update">
/// For a head with no options window, which holds the button on Windows, the update as an item on
/// the menu (<see cref="TrayUpdate.MenuAction" />). Null there too where the tray is not an
/// installed tool.
/// </param>
record TrayActions(
    Action Exit,
    Action? Options,
    Action DebugView,
    Action? Purge,
    Action<TrackedMove> OpenDiffTool,
    IRunAtLogin? RunAtLogin = null,
    Action? Update = null);
