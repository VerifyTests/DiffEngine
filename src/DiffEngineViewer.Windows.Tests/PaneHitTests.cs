/// <summary>
/// The painter and the hit test have to be reading one layout, or a drag selects one run of
/// characters and colours another. Rather than restate the arithmetic, this draws a selection and
/// feeds the pixels it landed on back through the hit test.
/// <para>
/// The pixel snapshots cannot catch that: they show where the highlight went, and say nothing
/// about where a click resolves to. This is the only test of the mapping in either direction.
/// </para>
/// </summary>
[NotInParallel]
[TUnit.Core.Executors.STAThreadExecutor]
public class PaneHitTests
{
    const int width = 1100;
    const int height = 700;

    /// <summary>
    /// The grid the other Windows captures are pinned to.
    /// </summary>
    const int columns = 120;

    const int rows = 37;

    [Test]
    public Task InTheReceivedPane() =>
        RoundTrip(PaneSide.Left);

    [Test]
    public Task InTheExpectedPane() =>
        RoundTrip(PaneSide.Right);

    /// <summary>
    /// Private rather than a parameterised test, because <see cref="PaneSide"/> is internal and a
    /// public test method cannot take one.
    /// </summary>
    static async Task RoundTrip(PaneSide side)
    {
        // One row, so the drawn run has exactly one top left corner to find.
        var state = ViewerSession.Resize(
            ViewerSession.Drag(Fixtures.File(), side, 1, 6, 1, 9),
            columns,
            rows);

        using var host = new Host();
        var bitmap = host.Draw(ScreenBuilder.Build(state));

        var drawn = TopLeftOf(bitmap, Palette.Selection);
        await Assert.That(drawn).IsNotNull();

        var cell = host.Canvas.PaneCellAt(drawn!.Value);

        await Assert.That(cell).IsNotNull();
        await Assert.That(cell!.Value.Side).IsEqualTo(side);
        await Assert.That(cell.Value.Row).IsEqualTo(1);
        await Assert.That(cell.Value.Column).IsEqualTo(6);
    }

    /// <summary>
    /// The queue column belongs to the row hit test, not to this one, so a point in it is not a
    /// pane cell however far down it is.
    /// </summary>
    [Test]
    public async Task TheQueueColumnIsNotAPaneCell()
    {
        var state = ViewerSession.Resize(Fixtures.Inline(Fixtures.Patch()), columns, rows);

        using var host = new Host();
        host.Draw(ScreenBuilder.Build(state));

        await Assert.That(host.Canvas.PaneCellAt(new(10, 200))).IsNull();
    }

    /// <summary>
    /// The top left pixel of the first run drawn in <paramref name="colour"/>, or null. Scanned
    /// top down and then left to right, so it is the corner rather than any pixel of the run.
    /// </summary>
    static Point? TopLeftOf(Bitmap bitmap, Color colour)
    {
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).ToArgb() == colour.ToArgb())
                {
                    return new(x, y);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// A canvas in a real window, because measuring a cell and answering a paint both need a
    /// handle. Parked off screen and never in the taskbar, so a run does not flash a window across
    /// the middle of the display.
    /// </summary>
    sealed class Host : IDisposable
    {
        readonly Form form = new()
        {
            StartPosition = FormStartPosition.Manual,
            Location = new(-2000, -2000),
            ShowInTaskbar = false,
            ClientSize = new(width, height)
        };

        public ViewerCanvas Canvas { get; } = new()
        {
            Dock = DockStyle.Fill
        };

        public Host()
        {
            form.Controls.Add(Canvas);
            form.Show();
        }

        public Bitmap Draw(Screen screen)
        {
            Canvas.Draw(screen);
            // Invalidate only marks dirty, and DrawToBitmap sends a paint message, so the paint
            // has to have happened before the bitmap.
            Canvas.Refresh();
            var bitmap = new Bitmap(Canvas.Width, Canvas.Height);
            Canvas.DrawToBitmap(bitmap, new(0, 0, Canvas.Width, Canvas.Height));
            bitmaps.Add(bitmap);
            return bitmap;
        }

        readonly List<Bitmap> bitmaps = [];

        public void Dispose()
        {
            foreach (var bitmap in bitmaps)
            {
                bitmap.Dispose();
            }

            form.Dispose();
        }
    }
}
