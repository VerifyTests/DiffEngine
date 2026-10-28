/// <summary>
/// What the tray's menu does that only a head can: show the head's own windows, end the app, and
/// start a move's diff tool, which on Windows brings the tool's window forward through user32.
/// </summary>
/// <param name="OpenDiffTool">Handed the move as it is when the item is chosen, with an executable.</param>
record TrayActions(
    Action Exit,
    Action Options,
    Action DebugView,
    Action Purge,
    Action<TrackedMove> OpenDiffTool);
