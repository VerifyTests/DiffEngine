/// <summary>
/// PlatformHeads' StatusNotifierItem with this tray's names and pictures, and the icon that says
/// something is pending marked as needing attention, for a panel that draws that differently.
/// </summary>
static class LinuxTray
{
    public static StatusNotifierTray<TrayIcon>? Open(out string? error) =>
        StatusNotifierTray<TrayIcon>.Open(
            new(
                "DiffEngineTray",
                "DiffEngineTray",
                TrayImages.Pixels,
                TrayImages.Glyph,
                _ => _ == TrayIcon.Active,
                Failed),
            out error);

    static void Failed(Exception exception, string what) =>
        Log.Warning(exception, "Tray: {What}", what);
}
