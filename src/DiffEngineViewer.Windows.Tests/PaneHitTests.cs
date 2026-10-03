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
    /// A document with its page under its text shows the text in the top half of the pane, and
    /// below that is the page: a press there is not a press on a row nobody can see.
    /// </summary>
    [Test]
    public async Task ThePageUnderTheTextIsNotText()
    {
        var left = new DocumentFile("temp/sample.received.pdf", 10, DocumentFormat.Pdf, "AA");
        var right = new DocumentFile("code/sample.verified.pdf", 11, DocumentFormat.Pdf, "BB");
        var entry = QueueEntry.ForFiles(
            "temp/sample.received.pdf",
            "code/sample.verified.pdf",
            new(Fixtures.Long(false), null, null, null, left),
            new(Fixtures.Long(true), null, null, null, right));
        var state = ViewerSession.Resize(
            ViewerSession.EnqueueFile(SessionState.Start(ViewerMode.File), entry),
            columns,
            rows);
        RenderedPage[] page = [new("render/page_0001.png", 600, 800, "PAGE")];
        state = ViewerSession.Rendered(state, "AA", new(page, true));
        state = ViewerSession.Rendered(state, "BB", new(page, true));

        using var host = new Host();
        host.Draw(ScreenBuilder.Build(state.Showing(DrawingView.Text)));
        var lowest = LowestCell(host.Canvas);
        await Assert.That(lowest).IsNotNull();

        host.Draw(ScreenBuilder.Build(state));
        await Assert.That(host.Canvas.PaneCellAt(new(width / 4, lowest!.Value))).IsNull();
    }

    /// <summary>
    /// A right click anywhere in a pane asks for that pane's menu: on its text, and under it, since
    /// a file of five lines has most of its pane under them. The queue column, the headers and the
    /// title are not a pane.
    /// </summary>
    [Test]
    public async Task ARightClickInAPaneAsksForItsMenu()
    {
        var state = ViewerSession.Resize(Fixtures.Inline(Fixtures.Patch()), columns, rows);
        using var host = new Host();
        host.Draw(ScreenBuilder.Build(state));
        var asked = new List<(PaneSide Side, Point At)>();
        host.Canvas.PaneRightClicked += (side, point) => asked.Add((side, point));

        // From the canvas as it is rather than from the size asked for: a window is no larger than
        // the display it is on, and a build agent's is smaller than this one asks to be.
        var canvas = host.Canvas;
        var onText = new Point(canvas.Width / 2, 75);
        var underText = new Point(canvas.Width - 60, canvas.Height - 120);
        host.RightClick(onText);
        host.RightClick(underText);
        host.RightClick(new(10, 200));
        host.RightClick(new(canvas.Width / 2, 10));

        await Assert.That(asked.Count).IsEqualTo(2);
        await Assert.That(asked[0]).IsEqualTo((PaneSide.Left, onText));
        await Assert.That(asked[1]).IsEqualTo((PaneSide.Right, underText));
    }

    /// <summary>
    /// File mode has no queue column, so its left pane starts at the window's edge.
    /// </summary>
    [Test]
    public async Task WithNoQueueTheLeftPaneStartsAtTheEdge()
    {
        var state = ViewerSession.Resize(Fixtures.File(), columns, rows);
        using var host = new Host();
        host.Draw(ScreenBuilder.Build(state));

        await Assert.That(host.Canvas.PaneAt(new(10, 200))).IsEqualTo(PaneSide.Left);
        // The canvas's own width, which on a display narrower than the window asks to be is less
        await Assert.That(host.Canvas.PaneAt(new(host.Canvas.Width - 20, 200))).IsEqualTo(PaneSide.Right);
        await Assert.That(host.Canvas.PaneAt(new(10, 10))).IsNull();
    }

    /// <summary>
    /// The lowest y in the received pane that is a cell of the text.
    /// </summary>
    static int? LowestCell(ViewerCanvas canvas)
    {
        for (var y = canvas.Height - 1; y >= 0; y--)
        {
            if (canvas.PaneCellAt(new(width / 4, y)) is not null)
            {
                return y;
            }
        }

        return null;
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
        readonly Form form = new ParkedForm
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

        public void RightClick(Point at) =>
            SendMessage(Canvas.Handle, rightButtonDown, new(2), new((at.Y << 16) | (at.X & 0xFFFF)));

        const int rightButtonDown = 0x0204;

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

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
