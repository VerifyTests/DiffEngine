/// <summary>
/// The process wide WinForms setup.
/// <para>
/// Here rather than in <c>Program.Main</c> because two entry points need it: the app, and the test
/// host, which never runs Main and yet renders the same controls into the same committed
/// baselines. A host that configured itself differently would be photographing something no user
/// sees.
/// </para>
/// </summary>
static class ViewerApp
{
    static bool configured;

    /// <summary>
    /// The app: DPI aware, so the window follows whichever display it is on.
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

    static void Configure(HighDpiMode dpi)
    {
        // SetCompatibleTextRenderingDefault throws once a window exists, and there are two callers
        // that could both run.
        if (configured)
        {
            return;
        }

        configured = true;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // The reason this head exists rather than a sixth copy of the shim: the native renderer
        // has no DPI handling at all and converts pixels to cells by dividing by a constant.
        Application.SetHighDpiMode(dpi);
        // Last, and before any handle exists, because the mode is read as each control is created.
        // Without it the footer's real controls stay light against a dark canvas. The identifier is
        // not a diagnostic on the SDK this builds against; the suppression is insurance for a
        // machine whose reference pack still marks these APIs experimental.
#pragma warning disable WFO5001
        Application.SetColorMode(SystemColorMode.Dark);
#pragma warning restore WFO5001
    }
}
