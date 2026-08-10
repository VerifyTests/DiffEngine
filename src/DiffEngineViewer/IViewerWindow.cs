/// <summary>
/// One platform's renderer, and the only thing in the app that knows how pixels get drawn.
/// Everything above it is a pure function of <see cref="Screen" />, which is what lets
/// <see cref="AsciiRenderer" /> and its text snapshots stand in for every implementation.
/// <para>
/// The frame contract is deliberately coarse: one <see cref="Present" /> carrying a whole screen
/// and one <see cref="Poll" /> per iteration, rather than a stream of draw calls. That is what
/// makes a retained mode toolkit and an immediate mode renderer equally implementable behind it.
/// </para>
/// </summary>
interface IViewerWindow : IDisposable
{
    /// <summary>
    /// Draws one frame. False once the window has closed, which is how the loop learns to stop.
    /// </summary>
    bool Present(Screen screen);

    /// <summary>
    /// Everything the user did since the last call. Drains as it reads, so each event arrives once.
    /// </summary>
    ViewerInput Poll();

    void SetHidden(bool hidden);

    void Focus();

    /// <summary>
    /// Renders one frame offscreen to a PNG. Only the pixel snapshots use this.
    /// </summary>
    bool Capture(Screen screen, int width, int height, string pngPath);
}

/// <summary>
/// Opens the window for one platform. Null with an <paramref name="error" /> rather than an
/// exception, because a machine with no renderer for its RID and no desktop session to draw into
/// are both ordinary, and both want the same message rather than a stack trace.
/// </summary>
delegate IViewerWindow? OpenWindow(string title, int width, int height, bool hidden, out string? error);
