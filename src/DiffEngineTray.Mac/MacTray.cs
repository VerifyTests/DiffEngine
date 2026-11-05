/// <summary>
/// The tray's status bar item: PlatformHeads.Mac's <see cref="StatusItemTray{TIcon}" /> in the
/// tray's own native library, inside the accessory application <see cref="StatusItemApp" /> makes,
/// since this tray has no window and so no application of its own.
/// </summary>
static class MacTray
{
    /// <summary>
    /// The product of native/swift that links PlatformHeads' tray ABI, which this head carries under
    /// runtimes/{rid}/native.
    /// </summary>
    public const string Library = "diffengine_tray";

    /// <summary>
    /// The application started and the item in the status bar. Null with an <paramref name="error" />
    /// when there is no library for this machine, one of another version, or no status bar to put
    /// the item in.
    /// </summary>
    public static StatusItemTray<TrayIcon>? Open(out string? error)
    {
        StatusItemApp.SessionEndFailed = _ => Log.Error(_, "Failed while the session was ending");
        error = StatusItemApp.Start(Library, "Tracking what test runs leave pending");
        if (error is not null)
        {
            return null;
        }

        return StatusItemTray<TrayIcon>.Open(
            new(
                Library,
                "DiffEngineTray",
                (icon, _) => TrayPictures.Png(icon),
                TrayPictures.Glyph),
            out error);
    }
}
