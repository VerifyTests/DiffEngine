using System.ComponentModel;
using System.Drawing.Imaging;

/// <summary>
/// The head's canvas with no window around it, painted into a bitmap: what a paint costs, with
/// nothing put on the desktop to find out.
/// <para>
/// What runs is the canvas's own paint, handed a <see cref="Graphics"/> over the bitmap where a
/// window would hand it one over its double buffer.
/// </para>
/// </summary>
sealed class OffscreenCanvas : IDisposable
{
    /// <summary>
    /// Synchronous, as a capture is: there is no message loop here for a decode on the pool to be
    /// handed back through, so a picture is decoded and composed by the first paint that needs it.
    /// </summary>
    readonly ViewerCanvas canvas = new()
    {
        Synchronous = true
    };

    readonly PaintCaller caller = new();

    readonly Bitmap surface;

    public OffscreenCanvas(int width, int height)
    {
        canvas.Size = new(width, height);
        // Premultiplied, which is what the double buffer a window paints into holds.
        surface = new(width, height, PixelFormat.Format32bppPArgb);
    }

    /// <summary>
    /// What a window this size would be showing for <paramref name="state"/>: the grid the canvas
    /// reports, as <c>ViewerForm.Drain</c> reports it to the loop, and the screen built for that.
    /// </summary>
    public Screen Fit(SessionState state) =>
        ScreenBuilder.Build(
            ViewerSession.Resize(
                state,
                canvas.ColumnCapacity,
                canvas.BodyCapacity + ScreenBuilder.Chrome));

    /// <summary>
    /// Hands the canvas a screen, as the loop does on every frame that changed something.
    /// </summary>
    public void Show(Screen screen) =>
        canvas.Draw(screen);

    /// <summary>
    /// One paint of the whole canvas. A Graphics per paint, as a window's paint gets, so nothing
    /// one paint set on it is still set for the next.
    /// </summary>
    public void Paint()
    {
        using var graphics = Graphics.FromImage(surface);
        using var paint = new PaintEventArgs(graphics, new(Point.Empty, surface.Size));
        caller.Ask(canvas, paint);
    }

    public void Dispose()
    {
        canvas.Dispose();
        caller.Dispose();
        surface.Dispose();
    }

    /// <summary>
    /// A control, though never one with a handle, because <c>OnPaint</c> is protected and
    /// <see cref="Control.InvokePaint"/> is the door WinForms leaves for one control to ask
    /// another to paint.
    /// </summary>
    [DesignerCategory("")]
    sealed class PaintCaller : Control
    {
        public void Ask(Control control, PaintEventArgs paint) =>
            InvokePaint(control, paint);
    }
}
