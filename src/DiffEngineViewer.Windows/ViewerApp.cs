/// <summary>
/// The process wide WinForms setup, through the shell's <see cref="FormsApp"/>.
/// <para>
/// Here rather than in <c>Program.Main</c> because two entry points need it: the app, and the test
/// host, which never runs Main and yet renders the same controls into the same committed
/// baselines. A host that configured itself differently would be photographing something no user
/// sees.
/// </para>
/// </summary>
static class ViewerApp
{
    /// <summary>
    /// The app: DPI aware, so the window follows whichever display it is on. That is the reason
    /// this head exists rather than a sixth copy of the shim: the native renderer has no DPI
    /// handling at all and converts pixels to cells by dividing by a constant.
    /// </summary>
    public static void Configure() =>
        Configure(HighDpiMode.PerMonitorV2);

    /// <summary>
    /// The same rendering, pinned to one scale. What the capture host uses: <c>ViewerForm</c> sizes
    /// its footer with <c>LogicalToDeviceUnits</c>, so a DPI aware host would scale it by the
    /// capturing machine's display and turn every committed baseline into a picture of that
    /// machine rather than of the app.
    /// </summary>
    public static void ConfigureUnscaled() =>
        Configure(HighDpiMode.DpiUnaware);

    /// <summary>
    /// Dark always, or the footer's real controls stay light against a dark canvas.
    /// </summary>
    static void Configure(HighDpiMode dpi) =>
        FormsApp.Configure(dpi, SystemColorMode.Dark);
}
